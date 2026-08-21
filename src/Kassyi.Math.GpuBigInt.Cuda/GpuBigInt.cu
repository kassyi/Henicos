#include "GpuBigInt.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <math.h>
#include <string>
#include <sstream>
#include <vector>
#include <unordered_map>

cufftHandle planR2C;
cufftHandle planC2R;
double* d_real = nullptr;
cufftDoubleComplex* d_complex = nullptr;
unsigned int* d_data = nullptr;
void* d_cufftWorkspace = nullptr;

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

void CleanupGPU_Internal();

bool InitializeGPU_Internal(int E) {
    CleanupGPU_Internal();

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
    
    cufftCreate(&planR2C);
    cufftSetAutoAllocation(planR2C, 0);
    size_t workSizeR2C;
    cufftResult res1 = cufftMakePlan1d(planR2C, g_M, CUFFT_D2Z, 1, &workSizeR2C);
    
    cufftCreate(&planC2R);
    cufftSetAutoAllocation(planC2R, 0);
    size_t workSizeC2R;
    cufftResult res2 = cufftMakePlan1d(planC2R, g_M, CUFFT_Z2D, 1, &workSizeC2R);
    
    if (res1 != CUFFT_SUCCESS || res2 != CUFFT_SUCCESS) {
        printf("cuFFT Plan creation failed. res1=%d res2=%d\n", res1, res2);
        return false;
    }
    
    size_t maxWorkSize = (workSizeR2C > workSizeC2R) ? workSizeR2C : workSizeC2R;
    cudaMalloc(&d_cufftWorkspace, maxWorkSize);
    cufftSetWorkArea(planR2C, d_cufftWorkspace);
    cufftSetWorkArea(planC2R, d_cufftWorkspace);
    
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
bool MultiplySquare_Internal(unsigned int* data, int numWords, cudaStream_t stream = 0) {
    if (!d_real || !d_complex || !d_data) return false;
    
    // 2. Unpack uint array to double array (Base 2^16)
    int blockSize = 256;
    int gridSize = (g_M + blockSize - 1) / blockSize;
    
    // 1. Unpack uint array to double array
    UnpackKernel16<<<gridSize, blockSize, 0, stream>>>(data, d_real, g_M, numWords);
    cudaError_t err1 = cudaGetLastError();
    if (err1 != cudaSuccess) printf("Error UnpackKernel16: %s\n", cudaGetErrorString(err1));
    
    // 3. FFT (Real to Complex)
    cufftResult res1 = cufftExecD2Z(planR2C, d_real, d_complex);
    if (res1 != CUFFT_SUCCESS) {
        printf("cufftExecD2Z failed: %d\n", res1);
    }
    cudaError_t err2 = cudaGetLastError();
    if (err2 != cudaSuccess) printf("Error cufftExecD2Z: %s\n", cudaGetErrorString(err2));
    
    // 4. Pointwise Square
    int complexGrid = (g_ComplexM + blockSize - 1) / blockSize;
    double scale = 1.0 / g_M;
    PointwiseSquareKernel<<<complexGrid, blockSize, 0, stream>>>(d_complex, g_ComplexM, scale);
    cudaError_t err3 = cudaGetLastError();
    if (err3 != cudaSuccess) printf("Error PointwiseSquareKernel: %s\n", cudaGetErrorString(err3));
    
    // 5. IFFT (Complex to Real)
    cufftResult res2 = cufftExecZ2D(planC2R, d_complex, d_real);
    if (res2 != CUFFT_SUCCESS) {
        printf("cufftExecZ2D failed: %d\n", res2);
    }
    cudaError_t err4 = cudaGetLastError();
    if (err4 != cudaSuccess) printf("Error cufftExecZ2D: %s\n", cudaGetErrorString(err4));
    
    // 6. Resolve Carry and Pack back to uint
    // The maximum possible result is roughly numWords*2 length in 32-bit words
    int outNumWords = numWords * 2;
    cudaMemsetAsync(data, 0, sizeof(unsigned int) * outNumWords, stream);
    cudaError_t err5 = cudaGetLastError();
    if (err5 != cudaSuccess) printf("Error cudaMemsetAsync: %s\n", cudaGetErrorString(err5));
    
    ResolveCarryAndPackKernel16<<<1, 1, 0, stream>>>(d_real, data, g_M, outNumWords);
    cudaError_t err6 = cudaGetLastError();
    if (err6 != cudaSuccess) printf("Error ResolveCarryAndPackKernel16: %s\n", cudaGetErrorString(err6));
    
    return true;
}

void CleanupGPU_Internal() {
    if (d_real) { cudaFree(d_real); d_real = nullptr; }
    if (d_complex) { cudaFree(d_complex); d_complex = nullptr; }
    if (d_data) { cudaFree(d_data); d_data = nullptr; }
    if (g_M > 0) {
        if (d_cufftWorkspace) { cudaFree(d_cufftWorkspace); d_cufftWorkspace = nullptr; }
        if (planR2C) { cufftDestroy(planR2C); planR2C = 0; }
        if (planC2R) { cufftDestroy(planC2R); planC2R = 0; }
        g_M = 0;
    }
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
            
            rippleCarry += carryBuffer[chunkIdx];
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
            
            rippleBorrow += carryBuffer[chunkIdx];
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

    EXPORT_API bool GpuBigInt_CopyHostToDevice(unsigned int* d_dst, const unsigned int* h_src, int numWords, cudaStream_t stream) {
        return cudaMemcpyAsync(d_dst, h_src, sizeof(unsigned int) * numWords, cudaMemcpyHostToDevice, stream) == cudaSuccess;
    }

    EXPORT_API bool GpuBigInt_CopyDeviceToHost(unsigned int* h_dst, const unsigned int* d_src, int numWords, cudaStream_t stream) {
        return cudaMemcpyAsync(h_dst, d_src, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToHost, stream) == cudaSuccess;
    }

    EXPORT_API bool GpuBigInt_CopyDeviceToDevice(unsigned int* d_dst, const unsigned int* d_src, int numWords, cudaStream_t stream) {
        return cudaMemcpyAsync(d_dst, d_src, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToDevice, stream) == cudaSuccess;
    }

    __global__ void BitwiseAndKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords) {
        int i = blockIdx.x * blockDim.x + threadIdx.x;
        if (i < numWords) {
            d_result[i] = d_a[i] & d_b[i];
        }
    }

    EXPORT_API bool GpuBigInt_BitwiseAnd(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        BitwiseAndKernel<<<gridSize, blockSize, 0, stream>>>(d_a, d_b, d_result, numWords);
        return true;
    }

    EXPORT_API bool GpuBigInt_Square(unsigned int* data, int numWords, cudaStream_t stream) { 
        return MultiplySquare_Internal(data, numWords, stream); 
    }

    EXPORT_API bool GpuBigInt_Add(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int chunkSize = 256; // Words per chunk thread
        int numChunks = (numWords + chunkSize - 1) / chunkSize;
        int blockSize = 256;
        int gridSize = (numChunks + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        cudaMemsetAsync(d_genericCarryBuffer, 0, sizeof(long long) * (numChunks + 1), stream);
        ChunkAddKernel<<<gridSize, blockSize, 0, stream>>>(d_a, d_b, d_result, d_genericCarryBuffer, numWords, chunkSize);
        ApplyAddCarryKernel<<<1, 1, 0, stream>>>(d_result, d_genericCarryBuffer, numChunks, numWords, chunkSize);
        return true;
    }

    EXPORT_API bool GpuBigInt_Sub(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int chunkSize = 256;
        int numChunks = (numWords + chunkSize - 1) / chunkSize;
        int blockSize = 256;
        int gridSize = (numChunks + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        cudaMemsetAsync(d_genericCarryBuffer, 0, sizeof(long long) * (numChunks + 1), stream);
        ChunkSubKernel<<<gridSize, blockSize, 0, stream>>>(d_a, d_b, d_result, d_genericCarryBuffer, numWords, chunkSize);
        ApplySubBorrowKernel<<<1, 1, 0, stream>>>(d_result, d_genericCarryBuffer, numChunks, numWords, chunkSize);
        return true;
    }

    EXPORT_API bool GpuBigInt_ShiftLeft(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int wordShift = shiftBits / 32;
        int bitShift = shiftBits % 32;
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        ShiftLeftKernel<<<gridSize, blockSize, 0, stream>>>(d_a, wordShift, bitShift, d_result, numWords);
        return true;
    }

    EXPORT_API bool GpuBigInt_ShiftRight(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int wordShift = shiftBits / 32;
        int bitShift = shiftBits % 32;
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        
        ShiftRightKernel<<<gridSize, blockSize, 0, stream>>>(d_a, wordShift, bitShift, d_result, numWords);
        return true;
    }
    EXPORT_API bool GpuBigInt_FermatPRPTest(int E, unsigned int* out_data) {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 4;

        if (!d_real) return false; // Ensure GPU is initialized

        unsigned int *d_T, *d_Q, *d_R, *d_Temp, *d_MaskR;
        cudaMalloc((void**)&d_T, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_Q, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_R, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_Temp, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_MaskR, sizeof(unsigned int) * outNumWords);

        cudaMemsetAsync(d_T, 0, sizeof(unsigned int) * outNumWords);
        cudaMemsetAsync(d_Q, 0, sizeof(unsigned int) * outNumWords);
        cudaMemsetAsync(d_R, 0, sizeof(unsigned int) * outNumWords);
        cudaMemsetAsync(d_Temp, 0, sizeof(unsigned int) * outNumWords);
        cudaMemsetAsync(d_MaskR, 0, sizeof(unsigned int) * outNumWords);

        unsigned int val2 = 2;
        cudaMemcpyAsync(d_T, &val2, sizeof(unsigned int), cudaMemcpyHostToDevice);

        int bit2E1 = 2 * E + 1;
        int word2E1 = bit2E1 / 32;
        int rem2E1 = bit2E1 % 32;
        unsigned int* h_mask = new unsigned int[outNumWords]();
        for (int i = 0; i < word2E1; i++) h_mask[i] = 0xFFFFFFFF;
        if (rem2E1 > 0) h_mask[word2E1] = (1U << rem2E1) - 1;
        cudaMemcpyAsync(d_MaskR, h_mask, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);

        auto SquareAndReduce = [&]() {
            GpuBigInt_Square(d_T, numWords + 1);
            for (int pass = 0; pass < 3; pass++) {
                GpuBigInt_ShiftRight(d_T, bit2E1, d_Q, outNumWords);
                GpuBigInt_BitwiseAnd(d_T, d_MaskR, d_R, outNumWords);
                cudaMemcpyAsync(d_T, d_R, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToDevice);
                GpuBigInt_ShiftLeft(d_Q, E, d_Temp, outNumWords);
                GpuBigInt_Add(d_T, d_Temp, d_T, outNumWords);
                GpuBigInt_Sub(d_T, d_Q, d_T, outNumWords);
                GpuBigInt_ShiftLeft(d_Q, 1, d_Temp, outNumWords);
                GpuBigInt_Sub(d_T, d_Temp, d_T, outNumWords);
                GpuBigInt_ShiftLeft(d_Q, 3, d_Temp, outNumWords);
                GpuBigInt_Sub(d_T, d_Temp, d_T, outNumWords);
            }
        };

        auto MultiplyBy2AndReduce = [&]() {
            GpuBigInt_ShiftLeft(d_T, 1, d_T, outNumWords);
            GpuBigInt_ShiftRight(d_T, bit2E1, d_Q, outNumWords);
            GpuBigInt_BitwiseAnd(d_T, d_MaskR, d_R, outNumWords);
            cudaMemcpyAsync(d_T, d_R, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToDevice);
            GpuBigInt_ShiftLeft(d_Q, E, d_Temp, outNumWords);
            GpuBigInt_Add(d_T, d_Temp, d_T, outNumWords);
            GpuBigInt_Sub(d_T, d_Q, d_T, outNumWords);
            GpuBigInt_ShiftLeft(d_Q, 1, d_Temp, outNumWords);
            GpuBigInt_Sub(d_T, d_Temp, d_T, outNumWords);
            GpuBigInt_ShiftLeft(d_Q, 3, d_Temp, outNumWords);
            GpuBigInt_Sub(d_T, d_Temp, d_T, outNumWords);
        };

        for (int i = 0; i < E; i++) {
            SquareAndReduce();
            MultiplyBy2AndReduce();
        }

        for (int i = 0; i < E - 4; i++) {
            SquareAndReduce();
        }

        SquareAndReduce(); MultiplyBy2AndReduce(); // bit 3 (1)
        SquareAndReduce(); // bit 2 (0)
        SquareAndReduce(); MultiplyBy2AndReduce(); // bit 1 (1)
        SquareAndReduce(); // bit 0 (0)

        cudaDeviceSynchronize();
        cudaMemcpy(out_data, d_T, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);

        cudaFree(d_T);
        cudaFree(d_Q);
        cudaFree(d_R);
        cudaFree(d_Temp);
        cudaFree(d_MaskR);
        delete[] h_mask;

        return true;
    }

    EXPORT_API bool GpuBigInt_ExecuteIL(int E, const char* ilScript, unsigned int* out_data) {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; // Add generous padding
        
        // Re-initialize for this exact E to ensure optimal FFT sizes
        if (!InitializeGPU_Internal(E)) return false;

        std::unordered_map<std::string, unsigned int*> vars;
        
        // Pre-scan IL to find all required variables
        std::istringstream stream_prescan(ilScript);
        std::string pre_line;
        while (std::getline(stream_prescan, pre_line)) {
            if (pre_line.empty() || pre_line[0] == '\r') continue;
            std::istringstream iss(pre_line);
            std::string op;
            iss >> op;
            if (op == "SQUARE" || op == "COPY") {
                std::string dest, src; iss >> dest >> src;
                vars[dest] = nullptr; vars[src] = nullptr;
            } else if (op == "SHR" || op == "SHL") {
                std::string dest, src; int bits; iss >> dest >> src >> bits;
                vars[dest] = nullptr; vars[src] = nullptr;
            } else if (op == "ADD" || op == "SUB" || op == "AND") {
                std::string dest, a, b; iss >> dest >> a >> b;
                vars[dest] = nullptr; vars[a] = nullptr; vars[b] = nullptr;
            }
        }
        
        // Ensure critical registers exist even if not used in IL
        vars["T"] = nullptr;
        vars["MaskR"] = nullptr;
        vars["N"] = nullptr;

        for (auto& pair : vars) {
            cudaMalloc((void**)&pair.second, sizeof(unsigned int) * outNumWords * 2);
            cudaMemsetAsync(pair.second, 0, sizeof(unsigned int) * outNumWords * 2);
        }

        unsigned int val2 = 2;
        cudaMemcpy(vars["T"], &val2, sizeof(unsigned int), cudaMemcpyHostToDevice);

        int bit2E1 = 2 * E + 1;
        int word2E1 = bit2E1 / 32;
        int rem2E1 = bit2E1 % 32;
        unsigned int* h_mask = new unsigned int[outNumWords]();
        for (int i = 0; i < word2E1; i++) h_mask[i] = 0xFFFFFFFF;
        if (rem2E1 > 0) h_mask[word2E1] = (1U << rem2E1) - 1;
        cudaMemcpy(vars["MaskR"], h_mask, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);

        unsigned int* h_N = new unsigned int[outNumWords]();
        h_N[0] = 11;
        h_N[word2E1] |= (1U << rem2E1);
        int wordE = E / 32;
        int remE = E % 32;
        // Sub 2^E
        unsigned int borrow = 0;
        unsigned int subVal = (1U << remE);
        for (int i = 0; i < outNumWords; i++) {
            unsigned int s = (i == wordE) ? subVal : 0;
            long long diff = (long long)h_N[i] - s - borrow;
            if (diff < 0) {
                diff += 0x100000000LL;
                borrow = 1;
            } else {
                borrow = 0;
            }
            h_N[i] = (unsigned int)diff;
        }
        cudaMemcpy(vars["N"], h_N, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);
        delete[] h_N;

        cudaStream_t stream;
        cudaStreamCreate(&stream);

        // Define the block to interpret one snippet of IL
        auto runSnippet = [&](const std::string& snippet) {
            std::istringstream stream_il(snippet);
            std::string line;
            while (std::getline(stream_il, line)) {
                if (line.empty() || line[0] == '\r') continue;
                std::istringstream iss(line);
                std::string op;
                iss >> op;
                if (op == "SQUARE") {
                    std::string dest, src; iss >> dest >> src;
                    if (dest != src) {
                        cudaMemcpyAsync(vars[dest], vars[src], sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToDevice, stream);
                    }
                    GpuBigInt_Square(vars[dest], numWords + 1, stream);
                }
                else if (op == "SHR") {
                    std::string dest, src; int bits; iss >> dest >> src >> bits;
                    GpuBigInt_ShiftRight(vars[src], bits, vars[dest], outNumWords, stream);
                }
                else if (op == "SHL") {
                    std::string dest, src; int bits; iss >> dest >> src >> bits;
                    GpuBigInt_ShiftLeft(vars[src], bits, vars[dest], outNumWords, stream);
                }
                else if (op == "AND") {
                    std::string dest, src, mask; iss >> dest >> src >> mask;
                    GpuBigInt_BitwiseAnd(vars[src], vars[mask], vars[dest], outNumWords, stream);
                }
                else if (op == "ADD") {
                    std::string dest, a, b; iss >> dest >> a >> b;
                    GpuBigInt_Add(vars[a], vars[b], vars[dest], outNumWords, stream);
                }
                else if (op == "SUB") {
                    std::string dest, a, b; iss >> dest >> a >> b;
                    GpuBigInt_Sub(vars[a], vars[b], vars[dest], outNumWords, stream);
                }
                else if (op == "COPY") {
                    std::string dest, src; iss >> dest >> src;
                    cudaMemcpyAsync(vars[dest], vars[src], sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToDevice, stream);
                }
                cudaError_t err = cudaGetLastError();
                if (err != cudaSuccess) {
                    printf("Error after %s: %s\n", op.c_str(), cudaGetErrorString(err));
                }
            }
        };

        // Parse full IL into segments
        std::vector<std::string> segments; // 0=Before Loop, 1=LoopE, 2=LoopEMinus4, 3=After Loop
        std::istringstream full_il(ilScript);
        std::string line;
        std::string currentSegment = "";
        while (std::getline(full_il, line)) {
            if (line.find("LOOP_E") != std::string::npos && line.find("END") == std::string::npos && line.find("MINUS") == std::string::npos) {
                segments.push_back(currentSegment); currentSegment = "";
            }
            else if (line.find("LOOP_E_MINUS_4") != std::string::npos) {
                segments.push_back(currentSegment); currentSegment = "";
            }
            else if (line.find("END_LOOP") != std::string::npos) {
                segments.push_back(currentSegment); currentSegment = "";
            }
            else {
                currentSegment += line + "\n";
            }
        }
        segments.push_back(currentSegment);
        // segments layout:
        // segments[0]: init
        // segments[1]: loopE body
        // segments[2]: inter
        // segments[3]: loopE-4 body
        // segments[4]: post

        auto captureAndExecute = [&](const std::string& snippet, int iterations) {
            if (iterations <= 0) return;
            cudaGraph_t graph;
            
            cufftSetStream(planR2C, stream);
            cufftSetStream(planC2R, stream);
            
            cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
            
            runSnippet(snippet);
            
            cudaError_t errEnd = cudaStreamEndCapture(stream, &graph);
            if (errEnd != cudaSuccess) {
                printf("cudaStreamEndCapture failed: %s\n", cudaGetErrorString(errEnd));
                return;
            }
            cudaGraphExec_t instance;
            cudaError_t errInst = cudaGraphInstantiate(&instance, graph, NULL, NULL, 0);
            if (errInst != cudaSuccess) {
                printf("cudaGraphInstantiate failed: %s\n", cudaGetErrorString(errInst));
                return;
            }
            
            for (int i = 0; i < iterations; i++) {
                cudaError_t glErr = cudaGraphLaunch(instance, stream);
                if (glErr != cudaSuccess) {
                    printf("cudaGraphLaunch failed at iter %d: %s\n", i, cudaGetErrorString(glErr));
                }
            }
            cudaError_t syncErr = cudaStreamSynchronize(stream);
            if (syncErr != cudaSuccess) {
                printf("cudaStreamSynchronize failed: %s\n", cudaGetErrorString(syncErr));
            }
            cudaGraphExecDestroy(instance);
            cudaGraphDestroy(graph);
        };
        
        runSnippet(segments[0]); // Run inline (no loop)
        cudaStreamSynchronize(stream);
        
        captureAndExecute(segments[1], E); // LOOP_E
        
        runSnippet(segments[2]);
        cudaStreamSynchronize(stream);
        
        captureAndExecute(segments[3], E - 4); // LOOP_E_MINUS_4
        
        runSnippet(segments[4]);
        cudaStreamSynchronize(stream);

        cudaMemcpy(out_data, vars["T"], sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);

        for (auto& pair : vars) {
            cudaFree(pair.second);
        }
        delete[] h_mask;
        cudaStreamDestroy(stream);
        cufftSetStream(planR2C, 0);
        cufftSetStream(planC2R, 0);

        return true;
    }
}
