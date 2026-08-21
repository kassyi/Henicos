#include "GpuBigInt.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <math.h>

cufftHandle planR2C;
cufftHandle planC2R;
double* d_real = nullptr;
cufftDoubleComplex* d_complex = nullptr;
unsigned int* d_data = nullptr;

int g_M = 0;          // FFT Size (must be a power of 2 for optimal performance)
int g_ComplexM = 0;   // Size of the complex array after Real-to-Complex FFT (M/2 + 1)

/**
 * @brief Finds the next power of 2 for a given integer.
 * Used to determine the optimal FFT size.
 */
int NextPowerOf2(int n) {
    n--;
    n |= n >> 1;
    n |= n >> 2;
    n |= n >> 4;
    n |= n >> 8;
    n |= n >> 16;
    n++;
    return n;
}

/**
 * @brief Unpacks a 32-bit unsigned int array into a double array using Base 2^16.
 * 
 * To avoid precision loss during the FFT convolution (which involves large floating point numbers),
 * we split each 32-bit word into two 16-bit halves (Base 65536) and cast them to double.
 * 
 * @param A Source 32-bit integer array.
 * @param B Destination double array.
 * @param M FFT Size (total elements in double array).
 * @param numWords Number of 32-bit words in the input.
 */
__global__ void UnpackKernel16(const unsigned int* A, double* B, int M, int numWords) {
    int k = blockIdx.x * blockDim.x + threadIdx.x;
    if (k < M) {
        double val = 0.0;
        int wordIdx = k / 2;
        if (wordIdx < numWords) {
             unsigned int w = A[wordIdx];
             if (k % 2 == 0) {
                 val = (double)(w & 0xFFFF);
             } else {
                 val = (double)(w >> 16);
             }
        }
        B[k] = val;
    }
}

/**
 * @brief Performs pointwise complex multiplication (Squaring) in the frequency domain.
 * 
 * Applies the formula (x + iy)^2 = (x^2 - y^2) + i(2xy).
 * Also scales the result by 1/M to normalize the upcoming Inverse FFT.
 * 
 * @param data Complex array from the cuFFT R2C transform.
 * @param numElements Size of the complex array (M/2 + 1).
 * @param scale Normalization factor (1.0 / M).
 */
__global__ void PointwiseSquareKernel(cufftDoubleComplex* data, int numElements, double scale) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numElements) {
        cufftDoubleComplex v = data[i];
        cufftDoubleComplex res;
        res.x = (v.x * v.x - v.y * v.y) * scale;
        res.y = (v.x * v.y + v.y * v.x) * scale;
        data[i] = res;
    }
}

/**
 * @brief Resolves carries and repacks the Base 2^16 double array back into a 32-bit uint array.
 * 
 * After the Inverse FFT, the double array contains the raw convolution values.
 * This kernel rounds them to nearest integers, adds the carry from the previous element,
 * and packs two 16-bit values back into a single 32-bit word.
 * 
 * @note Currently implemented as a single-threaded kernel (<<<1, 1>>>) because carry resolution
 *       is strictly sequential. For massive arrays, this may become a bottleneck and could be 
 *       replaced with a parallel Prefix Sum (Scan) approach.
 */
__global__ void ResolveCarryAndPackKernel16(double* d_real, unsigned int* out_uint, int M, int outNumWords) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid == 0) {
        // Clear output array
        for(int i=0; i<outNumWords; i++) out_uint[i] = 0;

        unsigned long long carry = 0;
        for (int k = 0; k < M; k++) {
            double raw = d_real[k];
            // Safe rounding
            unsigned long long val = (unsigned long long)(raw + 0.5) + carry;
            
            unsigned int lower16 = (unsigned int)(val & 0xFFFF);
            carry = val >> 16;
            
            int wordIdx = k / 2;
            if (wordIdx < outNumWords) {
                if (k % 2 == 0) {
                    out_uint[wordIdx] = lower16;
                } else {
                    out_uint[wordIdx] |= (lower16 << 16);
                }
            }
        }
        
        // Ripple the remaining carry beyond the FFT size
        int k = M;
        while (carry > 0) {
            int wordIdx = k / 2;
            if (wordIdx >= outNumWords) break;
            
            unsigned int lower16 = (unsigned int)(carry & 0xFFFF);
            carry >>= 16;
            
            if (k % 2 == 0) {
                out_uint[wordIdx] = lower16;
            } else {
                out_uint[wordIdx] |= (lower16 << 16);
            }
            k++;
        }
    }
}

bool InitializeGPU_Internal(int E) {
    // E determines the bit size: 2E+1 bits
    // numWords is around (2E+1)/32
    // Max value fits in 2E+1 bits. Squaring makes it 4E+2 bits.
    // Base is 2^16 (16 bits per element).
    // Total elements needed = (4E + 2 + 15) / 16.
    int requiredElements = (4 * E + 2 + 15) / 16;
    
    // We must use an FFT size that is at least requiredElements, and highly composite (e.g. power of 2)
    g_M = NextPowerOf2(requiredElements);
    g_ComplexM = g_M / 2 + 1;
    
    printf("Initializing cuFFT: E=%d, Required Radix-16 Elements=%d, FFT Size=%d\n", E, requiredElements, g_M);
    
    cufftResult res1 = cufftPlan1d(&planR2C, g_M, CUFFT_D2Z, 1);
    cufftResult res2 = cufftPlan1d(&planC2R, g_M, CUFFT_Z2D, 1);
    
    if (res1 != CUFFT_SUCCESS || res2 != CUFFT_SUCCESS) {
        printf("cuFFT Plan creation failed.\n");
        return false;
    }
    
    cudaMalloc((void**)&d_real, sizeof(double) * g_M);
    cudaMalloc((void**)&d_complex, sizeof(cufftDoubleComplex) * g_ComplexM);
    // Allocate space for uint buffer (we receive numWords, and we send back up to numWords*2)
    // We'll allocate a generous buffer
    int maxUintWords = (g_M * 16 + 31) / 32;
    cudaMalloc((void**)&d_data, sizeof(unsigned int) * maxUintWords);
    
    return true;
}

/**
 * @brief Performs a full FFT-based squaring of a BigInteger.
 * 
 * Pipeline:
 * 1. Unpack uint array to double array (Base 16).
 * 2. Forward FFT (Real to Complex).
 * 3. Pointwise Squaring in frequency domain.
 * 4. Inverse FFT (Complex to Real).
 * 5. Carry Resolution and Repacking to uint array.
 * 
 * @param data Device pointer to the BigInteger data (must be pre-allocated with enough space).
 * @param numWords Number of 32-bit words in the input.
 */
bool MultiplySquare_Internal(unsigned int* data, int numWords) {
    if (!d_real || !d_complex || !d_data) return false;
    
    // 1. Copy input from CPU to GPU (data only contains numWords elements for the input X)
    cudaMemcpy(d_data, data, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToDevice); // Fixed logical naming, technically it's an internal buffer copy if data is already device ptr
    
    // 2. Unpack uint array to double array (Base 2^16)
    int blockSize = 256;
    int gridSize = (g_M + blockSize - 1) / blockSize;
    UnpackKernel16<<<gridSize, blockSize>>>(data, d_real, g_M, numWords);
    cudaDeviceSynchronize();
    
    // 3. FFT (Real to Complex)
    cufftExecD2Z(planR2C, d_real, d_complex);
    cudaDeviceSynchronize();
    
    // 4. Pointwise Square
    int complexGrid = (g_ComplexM + blockSize - 1) / blockSize;
    double scale = 1.0 / g_M;
    PointwiseSquareKernel<<<complexGrid, blockSize>>>(d_complex, g_ComplexM, scale);
    cudaDeviceSynchronize();
    
    // 5. IFFT (Complex to Real)
    cufftExecZ2D(planC2R, d_complex, d_real);
    cudaDeviceSynchronize();
    
    // 6. Resolve Carry and Pack back to uint (Result takes roughly numWords*2 space)
    int outNumWords = numWords * 2 + 1; // Generous
    ResolveCarryAndPackKernel16<<<1, 1>>>(d_real, data, g_M, outNumWords);
    cudaDeviceSynchronize();
    
    return true;
}

void CleanupGPU_Internal() {
    if (d_real) cudaFree(d_real);
    if (d_complex) cudaFree(d_complex);
    if (d_data) cudaFree(d_data);
    cufftDestroy(planR2C);
    cufftDestroy(planC2R);
}

// -------------------------------------------------------------------------
// Generic Parallel Arithmetics (Add, Sub, Shift)
//
// To achieve maximum performance for millions of elements, we use a 2-Pass 
// architecture for Addition and Subtraction:
// Pass 1: Chunk-local operations. Each thread block computes additions 
//         and outputs a single chunk-carry to a global buffer.
// Pass 2: Global Ripple. A single thread scans the chunk-carries and 
//         ripples them through the target array where necessary.
// -------------------------------------------------------------------------

long long* d_genericCarryBuffer = nullptr;
int g_genericMaxChunks = 0;

/**
 * @brief (Pass 1) Computes addition of two BigIntegers in independent chunks.
 * 
 * Each thread handles a 'chunkSize' portion of the array. It performs standard addition
 * and stores the final carry-out of its chunk into the global 'carryBuffer'.
 * 
 * @param d_a First operand array.
 * @param d_b Second operand array.
 * @param d_result Output array.
 * @param carryBuffer Global buffer to store chunk carries.
 * @param numWords Total number of words.
 * @param chunkSize Number of words processed by each thread.
 */
__global__ void ChunkAddKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, long long* carryBuffer, int numWords, int chunkSize) {
    int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x;
    int start = chunkIdx * chunkSize;
    if (start >= numWords) return;
    
    int end = start + chunkSize;
    if (end > numWords) end = numWords;
    
    unsigned long long localCarry = 0;
    for (int i = start; i < end; i++) {
        unsigned long long current = (unsigned long long)d_a[i] + (unsigned long long)d_b[i] + localCarry;
        d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
        localCarry = current >> 32;
    }
    carryBuffer[chunkIdx + 1] = localCarry;
}

/**
 * @brief (Pass 2) Ripples the chunk carries across the result array.
 * 
 * Executed by a single thread (<<<1, 1>>>). It scans the carryBuffer and propagates
 * any ripple carries into the d_result array computed by Pass 1.
 */
__global__ void ApplyAddCarryKernel(unsigned int* d_result, long long* carryBuffer, int numChunks, int numWords, int chunkSize) {
    if (threadIdx.x == 0 && blockIdx.x == 0) {
        unsigned long long rippleCarry = 0;
        for (int chunkIdx = 0; chunkIdx < numChunks; chunkIdx++) {
            int start = chunkIdx * chunkSize;
            int end = start + chunkSize;
            if (end > numWords) end = numWords;
            
            rippleCarry += carryBuffer[chunkIdx + 1];
            if (rippleCarry == 0) continue;
            
            for (int i = start; i < end; i++) {
                unsigned long long current = (unsigned long long)d_result[i] + rippleCarry;
                d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
                rippleCarry = current >> 32;
                if (rippleCarry == 0) break;
            }
        }
    }
}

/**
 * @brief (Pass 1) Computes subtraction (A - B) in independent chunks.
 * 
 * Each thread handles a 'chunkSize' portion. It performs subtraction with borrow.
 * The final borrow-out of the chunk is written to 'carryBuffer'.
 */
__global__ void ChunkSubKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, long long* carryBuffer, int numWords, int chunkSize) {
    int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x;
    int start = chunkIdx * chunkSize;
    if (start >= numWords) return;
    
    int end = start + chunkSize;
    if (end > numWords) end = numWords;
    
    long long localBorrow = 0;
    for (int i = start; i < end; i++) {
        long long current = (long long)d_a[i] - (long long)d_b[i] - localBorrow;
        if (current < 0) {
            current += 0x100000000LL;
            localBorrow = 1;
        } else {
            localBorrow = 0;
        }
        d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
    }
    carryBuffer[chunkIdx + 1] = localBorrow;
}

/**
 * @brief (Pass 2) Ripples the chunk borrows across the result array.
 */
__global__ void ApplySubBorrowKernel(unsigned int* d_result, long long* carryBuffer, int numChunks, int numWords, int chunkSize) {
    if (threadIdx.x == 0 && blockIdx.x == 0) {
        long long rippleBorrow = 0;
        for (int chunkIdx = 0; chunkIdx < numChunks; chunkIdx++) {
            int start = chunkIdx * chunkSize;
            int end = start + chunkSize;
            if (end > numWords) end = numWords;
            
            rippleBorrow += carryBuffer[chunkIdx + 1];
            if (rippleBorrow == 0) continue;
            
            for (int i = start; i < end; i++) {
                long long current = (long long)d_result[i] - rippleBorrow;
                if (current < 0) {
                    current += 0x100000000LL;
                    rippleBorrow = 1;
                } else {
                    rippleBorrow = 0;
                }
                d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
                if (rippleBorrow == 0) break;
            }
        }
    }
}

/**
 * @brief Shifts a BigInteger left by a specified number of bits.
 * 
 * Since this is out-of-place (d_a != d_result), this can be executed fully in parallel 
 * without data races.
 * 
 * @param d_a Source array.
 * @param wordShift Number of full 32-bit words to shift.
 * @param bitShift Number of remaining bits to shift (0-31).
 * @param d_result Destination array.
 */
__global__ void ShiftLeftKernel(const unsigned int* d_a, int wordShift, int bitShift, unsigned int* d_result, int numWords) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        int srcIdx0 = i - wordShift;
        int srcIdx1 = srcIdx0 - 1;
        unsigned int w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? d_a[srcIdx0] : 0;
        unsigned int w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? d_a[srcIdx1] : 0;
        
        if (bitShift == 0) {
            d_result[i] = w0;
        } else {
            d_result[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
        }
    }
}

/**
 * @brief Shifts a BigInteger right by a specified number of bits.
 */
__global__ void ShiftRightKernel(const unsigned int* d_a, int wordShift, int bitShift, unsigned int* d_result, int numWords) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        int srcIdx0 = i + wordShift;
        int srcIdx1 = srcIdx0 + 1;
        unsigned int w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? d_a[srcIdx0] : 0;
        unsigned int w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? d_a[srcIdx1] : 0;
        
        if (bitShift == 0) {
            d_result[i] = w0;
        } else {
            d_result[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
        }
    }
}

// -------------------------------------------------------------------------
// Exported API (Callable from C# via P/Invoke)
// -------------------------------------------------------------------------

extern "C" {
    /**
     * @brief Initializes the GPU Context and allocates internal buffers for FFT and Carry logic.
     * @param maxWords The maximum size of the BigInteger (in 32-bit words) we intend to support.
     */
    EXPORT_API bool GpuBigInt_Init(int maxWords) {
        int simulatedE = (maxWords * 32) / 2; 
        bool res = InitializeGPU_Internal(simulatedE);
        
        int chunkSize = 256;
        g_genericMaxChunks = (maxWords + chunkSize - 1) / chunkSize;
        cudaMalloc((void**)&d_genericCarryBuffer, sizeof(long long) * (g_genericMaxChunks + 1));
        
        return res;
    }
    
    /**
     * @brief Cleans up GPU context and releases VRAM.
     */
    EXPORT_API void GpuBigInt_Cleanup() { 
        CleanupGPU_Internal(); 
        if (d_genericCarryBuffer) cudaFree(d_genericCarryBuffer);
    }
    
    EXPORT_API unsigned int* GpuBigInt_Alloc(int numWords) {
        unsigned int* ptr = nullptr;
        cudaMalloc((void**)&ptr, sizeof(unsigned int) * numWords);
        return ptr;
    }

    EXPORT_API void GpuBigInt_Free(unsigned int* ptr) {
        if (ptr) cudaFree(ptr);
    }

    EXPORT_API bool GpuBigInt_CopyHostToDevice(unsigned int* d_dst, const unsigned int* h_src, int numWords) {
        return cudaMemcpy(d_dst, h_src, sizeof(unsigned int) * numWords, cudaMemcpyHostToDevice) == cudaSuccess;
    }

    EXPORT_API bool GpuBigInt_CopyDeviceToHost(unsigned int* h_dst, const unsigned int* d_src, int numWords) {
        return cudaMemcpy(h_dst, d_src, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToHost) == cudaSuccess;
    }

    EXPORT_API bool GpuBigInt_CopyDeviceToDevice(unsigned int* d_dst, const unsigned int* d_src, int numWords) {
        return cudaMemcpy(d_dst, d_src, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToDevice) == cudaSuccess;
    }

    __global__ void BitwiseAndKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords) {
        int i = blockIdx.x * blockDim.x + threadIdx.x;
        if (i < numWords) {
            d_result[i] = d_a[i] & d_b[i];
        }
    }

    EXPORT_API bool GpuBigInt_BitwiseAnd(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords) {
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        BitwiseAndKernel<<<gridSize, blockSize>>>(d_a, d_b, d_result, numWords);
        cudaDeviceSynchronize();
        return true;
    }

    EXPORT_API bool GpuBigInt_Square(unsigned int* data, int numWords) { 
        return MultiplySquare_Internal(data, numWords); 
    }

    EXPORT_API bool GpuBigInt_Add(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords) {
        int chunkSize = 256; // Words per chunk thread
        int numChunks = (numWords + chunkSize - 1) / chunkSize;
        int blockSize = 256;
        int gridSize = (numChunks + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        cudaMemset(d_genericCarryBuffer, 0, sizeof(long long) * (numChunks + 1));
        ChunkAddKernel<<<gridSize, blockSize>>>(d_a, d_b, d_result, d_genericCarryBuffer, numWords, chunkSize);
        ApplyAddCarryKernel<<<1, 1>>>(d_result, d_genericCarryBuffer, numChunks, numWords, chunkSize);
        cudaDeviceSynchronize();
        return true;
    }

    EXPORT_API bool GpuBigInt_Sub(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords) {
        int chunkSize = 256;
        int numChunks = (numWords + chunkSize - 1) / chunkSize;
        int blockSize = 256;
        int gridSize = (numChunks + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        cudaMemset(d_genericCarryBuffer, 0, sizeof(long long) * (numChunks + 1));
        ChunkSubKernel<<<gridSize, blockSize>>>(d_a, d_b, d_result, d_genericCarryBuffer, numWords, chunkSize);
        ApplySubBorrowKernel<<<1, 1>>>(d_result, d_genericCarryBuffer, numChunks, numWords, chunkSize);
        cudaDeviceSynchronize();
        return true;
    }

    EXPORT_API bool GpuBigInt_ShiftLeft(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords) {
        int wordShift = shiftBits / 32;
        int bitShift = shiftBits % 32;
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        ShiftLeftKernel<<<gridSize, blockSize>>>(d_a, wordShift, bitShift, d_result, numWords);
        cudaDeviceSynchronize();
        return true;
    }

    EXPORT_API bool GpuBigInt_ShiftRight(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords) {
        int wordShift = shiftBits / 32;
        int bitShift = shiftBits % 32;
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        ShiftRightKernel<<<gridSize, blockSize>>>(d_a, wordShift, bitShift, d_result, numWords);
        cudaDeviceSynchronize();
        return true;
    }
}
