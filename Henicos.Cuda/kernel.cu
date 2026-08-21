#include "kernel.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <math.h>

cufftHandle planR2C;
cufftHandle planC2R;
double* d_real = nullptr;
cufftDoubleComplex* d_complex = nullptr;
unsigned int* d_data = nullptr;

int g_M = 0; // FFT Size
int g_ComplexM = 0; // Complex array size

// Find next power of 2 for FFT size
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

bool MultiplySquare_Internal(unsigned int* data, int numWords) {
    if (!d_real || !d_complex || !d_data) return false;
    
    // 1. Copy input from CPU to GPU (data only contains numWords elements for the input X)
    cudaMemcpy(d_data, data, sizeof(unsigned int) * numWords, cudaMemcpyHostToDevice);
    
    // 2. Unpack uint array to double array (Base 2^16)
    int blockSize = 256;
    int gridSize = (g_M + blockSize - 1) / blockSize;
    UnpackKernel16<<<gridSize, blockSize>>>(d_data, d_real, g_M, numWords);
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
    // Output space must match the size required by T = X^2
    int outNumWords = numWords * 2 + 1; // Generous
    ResolveCarryAndPackKernel16<<<1, 1>>>(d_real, d_data, g_M, outNumWords);
    cudaDeviceSynchronize();
    
    // 7. Copy back to CPU (Assume CPU provided a large enough buffer, usually numWords*4)
    cudaMemcpy(data, d_data, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);
    
    return true;
}

void CleanupGPU_Internal() {
    if (d_real) cudaFree(d_real);
    if (d_complex) cudaFree(d_complex);
    if (d_data) cudaFree(d_data);
    cufftDestroy(planR2C);
    cufftDestroy(planC2R);
}

extern "C" {
    EXPORT_API bool InitializeGPU(int E) { return InitializeGPU_Internal(E); }
    EXPORT_API bool MultiplySquare(unsigned int* data, int numWords) { return MultiplySquare_Internal(data, numWords); }
    EXPORT_API void CleanupGPU() { CleanupGPU_Internal(); }

    // --- V5 Kernels and Functions ---
    
    __device__ unsigned int GetRawQWord(const unsigned int* data, int qWordStart, int qBitStart, int qIndex, int dataLen) {
        int w0Idx = qWordStart + qIndex;
        int w1Idx = w0Idx + 1;
        unsigned int w0 = (w0Idx >= 0 && w0Idx < dataLen) ? data[w0Idx] : 0;
        unsigned int w1 = (w1Idx >= 0 && w1Idx < dataLen) ? data[w1Idx] : 0;
        if (qBitStart == 0) return w0;
        return (w0 >> qBitStart) | (w1 << (32 - qBitStart));
    }

    __device__ unsigned int GetShiftedQWord(const unsigned int* data, int qWordStart, int qBitStart, int targetIndex, int wordShift, int bitShift, int dataLen) {
        int qTargetWord = targetIndex - wordShift;
        if (qTargetWord < 0) return 0;
        long long totalBitIndexInQ = (long long)qTargetWord * 32 - bitShift;
        if (totalBitIndexInQ < 0) {
            unsigned int q0 = GetRawQWord(data, qWordStart, qBitStart, 0, dataLen);
            return q0 << bitShift;
        }
        int qWordIdx = (int)(totalBitIndexInQ / 32);
        int qBitRem = (int)(totalBitIndexInQ % 32);
        unsigned int qw0 = GetRawQWord(data, qWordStart, qBitStart, qWordIdx, dataLen);
        unsigned int qw1 = GetRawQWord(data, qWordStart, qBitStart, qWordIdx + 1, dataLen);
        if (qBitRem == 0) return qw0;
        return (qw0 >> qBitRem) | (qw1 << (32 - qBitRem));
    }

    __global__ void ReduceChunkKernel(const unsigned int* data, int E, int dataLen, unsigned int* nextData, long long* carryBuffer, int chunkSize) {
        int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x;
        int start = chunkIdx * chunkSize;
        if (start >= dataLen) return;
        
        int end = start + chunkSize;
        if (end > dataLen) end = dataLen;
        
        int bit2E1 = 2 * E + 1;
        int word2E1 = bit2E1 / 32;
        int rem2E1 = bit2E1 % 32;
        int wordE = E / 32;
        int remE = E % 32;
        
        long long localCarry = 0;
        
        for (int i = start; i < end; i++) {
            long long current = (long long)data[i] + localCarry + ((i == start) ? carryBuffer[chunkIdx + 1] : 0);
            
            unsigned int qShiftE = GetShiftedQWord(data, word2E1, rem2E1, i, wordE, remE, dataLen);
            current += qShiftE;
            
            unsigned int qWord = GetShiftedQWord(data, word2E1, rem2E1, i, 0, 0, dataLen);
            unsigned int qShift1 = GetShiftedQWord(data, word2E1, rem2E1, i, 0, 1, dataLen);
            unsigned int qShift3 = GetShiftedQWord(data, word2E1, rem2E1, i, 0, 3, dataLen);
            
            current -= qWord;
            current -= qShift1;
            current -= qShift3;
            
            nextData[i] = (unsigned int)(current & 0xFFFFFFFF);
            localCarry = current >> 32;
        }
        carryBuffer[chunkIdx + 1] = localCarry;
    }

    __global__ void ApplyCarryKernel(unsigned int* nextData, long long* carryBuffer, int numChunks, int dataLen, int chunkSize) {
        if (threadIdx.x == 0 && blockIdx.x == 0) {
            long long rippleCarry = 0;
            for (int chunkIdx = 0; chunkIdx < numChunks; chunkIdx++) {
                int start = chunkIdx * chunkSize;
                int end = start + chunkSize;
                if (end > dataLen) end = dataLen;
                
                rippleCarry += carryBuffer[chunkIdx + 1]; // carry generated by chunk i
                if (rippleCarry == 0) continue;
                
                for (int i = start; i < end; i++) {
                    long long current = (long long)nextData[i] + rippleCarry;
                    nextData[i] = (unsigned int)(current & 0xFFFFFFFF);
                    rippleCarry = current >> 32;
                    if (rippleCarry == 0) break;
                }
                carryBuffer[chunkIdx + 1] = 0; // carry applied to chunk i (not used anymore in v7 logic since rippleCarry handles it, but kept to prevent issues)
            }
        }
    }

    __global__ void CopyAndMaskRKernel(const unsigned int* data, unsigned int* nextData, int E, int dataLen) {
        int i = blockIdx.x * blockDim.x + threadIdx.x;
        if (i < dataLen) {
            int bit2E1 = 2 * E + 1;
            int word2E1 = bit2E1 / 32;
            int rem2E1 = bit2E1 % 32;
            unsigned int rMask = (rem2E1 == 0) ? 0xFFFFFFFF : (1U << rem2E1) - 1;
            
            if (i < word2E1) {
                nextData[i] = data[i];
            } else if (i == word2E1) {
                nextData[i] = data[i] & rMask;
            } else {
                nextData[i] = 0;
            }
        }
    }

    __global__ void CopyBackKernel(unsigned int* data, const unsigned int* nextData, int dataLen) {
        int i = blockIdx.x * blockDim.x + threadIdx.x;
        if (i < dataLen) {
            data[i] = nextData[i];
        }
    }

    __device__ int d_normAction;
    __device__ bool d_normStillNegative;

    __global__ void EvalNormActionKernel(unsigned int* data, int numWords, int E) {
        if (threadIdx.x == 0 && blockIdx.x == 0) {
            unsigned int msbWord = data[numWords - 1];
            bool isNegative = (msbWord & 0x80000000) != 0;
            if (isNegative) {
                d_normAction = 1; // addN
            } else {
                d_normAction = -1; // subN
            }
        }
    }

    __global__ void EvalStillNegativeKernel(unsigned int* data, int numWords, int E) {
        if (threadIdx.x == 0 && blockIdx.x == 0) {
            unsigned int msbWord = data[numWords - 1];
            d_normStillNegative = (msbWord & 0x80000000) != 0;
            if (d_normStillNegative) {
                d_normAction = 1; // addN
            } else {
                d_normAction = 0; // stop
            }
        }
    }

    __global__ void MaskMsbKernel(unsigned int* data, int numWords, int E) {
        if (threadIdx.x == 0 && blockIdx.x == 0) {
            int bit2E1 = 2 * E + 1;
            int rem2E1 = bit2E1 % 32;
            data[numWords - 1] &= (rem2E1 == 0) ? 0xFFFFFFFFU : ((1U << rem2E1) - 1);
        }
    }

    __global__ void NormalizeChunkKernel(unsigned int* data, int numWords, int E, unsigned int* nextData, long long* carryBuffer, int chunkSize) {
        int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x;
        int start = chunkIdx * chunkSize;
        if (start >= numWords) return;
        
        int action = d_normAction;
        if (action == 0) {
            // Copy data if no action
            for (int i = start; i < start + chunkSize && i < numWords; i++) {
                nextData[i] = data[i];
            }
            carryBuffer[chunkIdx + 1] = 0;
            return;
        }

        int end = start + chunkSize;
        if (end > numWords) end = numWords;
        
        long long localCarry = 0;
        long long baseCarry = (start == 0) ? (action == 1 ? 11LL : -11LL) : 0;
        
        int wordE = E / 32;
        int word2E1 = (2 * E + 1) / 32;
        
        for (int i = start; i < end; i++) {
            long long current = (long long)data[i] + localCarry + ((i == start) ? baseCarry : 0) + ((i == start) ? carryBuffer[chunkIdx + 1] : 0);
            
            if (i == wordE) {
                current += (action == 1 ? -(1LL << (E % 32)) : +(1LL << (E % 32)));
            }
            if (i == word2E1) {
                current += (action == 1 ? +(1LL << ((2 * E + 1) % 32)) : -(1LL << ((2 * E + 1) % 32)));
            }
            
            nextData[i] = (unsigned int)(current & 0xFFFFFFFF);
            localCarry = current >> 32;
        }
        carryBuffer[chunkIdx + 1] = localCarry;
    }

    __global__ void MultiplyBy2KernelParallel(unsigned int* data, unsigned int* nextData, int numWords) {
        int i = blockIdx.x * blockDim.x + threadIdx.x;
        if (i < numWords) {
            unsigned int val = data[i];
            unsigned int carry = 0;
            if (i > 0) {
                carry = data[i - 1] >> 31;
            }
            nextData[i] = (val << 1) | carry;
        }
    }

    EXPORT_API bool RunFermatLoopV5(unsigned int* data, int numWords, int E) {
        if (!d_real || !d_complex || !d_data) return false;
        
        int outNumWords = numWords * 2 + 1;
        unsigned int* d_nextData = nullptr;
        cudaMalloc((void**)&d_nextData, sizeof(unsigned int) * outNumWords);

        // Dynamic Chunking: Target at least 1024 chunks to saturate the SMs
        int targetChunks = 1024;
        int chunkSize = (outNumWords + targetChunks - 1) / targetChunks;
        if (chunkSize < 4) chunkSize = 4;
        int numChunks = (outNumWords + chunkSize - 1) / chunkSize;
        
        long long* d_carryBuffer = nullptr;
        cudaMalloc((void**)&d_carryBuffer, sizeof(long long) * (numChunks + 1));

        cudaStream_t stream;
        cudaStreamCreate(&stream);
        cufftSetStream(planR2C, stream);
        cufftSetStream(planC2R, stream);

        auto runReduceInPlace = [&]() {
            for (int pass = 0; pass < 2; pass++) {
                cudaMemsetAsync(d_carryBuffer, 0, sizeof(long long) * (numChunks + 1), stream);
                CopyAndMaskRKernel<<<(outNumWords + 255) / 256, 256, 0, stream>>>(d_data, d_nextData, E, outNumWords);
                int reduceGrid = (numChunks + 255) / 256;
                if (reduceGrid == 0) reduceGrid = 1;
                ReduceChunkKernel<<<reduceGrid, 256, 0, stream>>>(d_data, E, outNumWords, d_nextData, d_carryBuffer, chunkSize);
                ApplyCarryKernel<<<1, 1, 0, stream>>>(d_nextData, d_carryBuffer, numChunks, outNumWords, chunkSize);
                CopyBackKernel<<<(outNumWords + 255) / 256, 256, 0, stream>>>(d_data, d_nextData, outNumWords);
            }
        };

        auto runNormalize = [&]() {
            int normGrid = (numChunks + 255) / 256;
            if (normGrid == 0) normGrid = 1;
            
            EvalNormActionKernel<<<1, 1, 0, stream>>>(d_data, numWords, E);
            
            for (int loop = 0; loop < 2; loop++) {
                cudaMemsetAsync(d_carryBuffer, 0, sizeof(long long) * (numChunks + 1), stream);
                NormalizeChunkKernel<<<normGrid, 256, 0, stream>>>(d_data, numWords, E, d_nextData, d_carryBuffer, chunkSize);
                ApplyCarryKernel<<<1, 1, 0, stream>>>(d_nextData, d_carryBuffer, numChunks, numWords, chunkSize);
                CopyBackKernel<<<(numWords + 255) / 256, 256, 0, stream>>>(d_data, d_nextData, numWords);
                
                if (loop == 0) {
                    EvalStillNegativeKernel<<<1, 1, 0, stream>>>(d_data, numWords, E);
                }
            }
            MaskMsbKernel<<<1, 1, 0, stream>>>(d_data, numWords, E);
        };

        // 1. Copy initial data (the number 2) from CPU to GPU
        cudaMemcpy(d_data, data, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);
        
        int blockSize = 256;
        int unpackGrid = (g_M + blockSize - 1) / blockSize;
        int complexGrid = (g_ComplexM + blockSize - 1) / blockSize;
        double scale = 1.0 / g_M;

        auto runSquare = [&]() {
            UnpackKernel16<<<unpackGrid, blockSize, 0, stream>>>(d_data, d_real, g_M, numWords);
            cufftExecD2Z(planR2C, d_real, d_complex);
            PointwiseSquareKernel<<<complexGrid, blockSize, 0, stream>>>(d_complex, g_ComplexM, scale);
            cufftExecZ2D(planC2R, d_complex, d_real);
            ResolveCarryAndPackKernel16<<<1, 1, 0, stream>>>(d_real, d_data, g_M, outNumWords);
            
            runReduceInPlace();
            runNormalize();
        };

        auto runMul2 = [&]() {
            int mulGrid = (numWords + 255) / 256;
            MultiplyBy2KernelParallel<<<mulGrid, 256, 0, stream>>>(d_data, d_nextData, numWords);
            CopyBackKernel<<<(numWords + 255) / 256, 256, 0, stream>>>(d_data, d_nextData, numWords);
            
            runReduceInPlace();
            runNormalize();
        };

        // --- CUDA Graphs Capture ---
        cudaGraph_t graphSquare;
        cudaGraphExec_t execSquare;
        cudaGraph_t graphSquareMul2;
        cudaGraphExec_t execSquareMul2;

        // Capture Square
        cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
        runSquare();
        cudaStreamEndCapture(stream, &graphSquare);
        cudaGraphInstantiate(&execSquare, graphSquare, NULL, NULL, 0);

        // Capture Square + Mul2
        cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
        runSquare();
        runMul2();
        cudaStreamEndCapture(stream, &graphSquareMul2);
        cudaGraphInstantiate(&execSquareMul2, graphSquareMul2, NULL, NULL, 0);

        // 1. MSB '1's sequence
        for (int i = 0; i < E; i++) {
            cudaGraphLaunch(execSquareMul2, stream);
        }

        // 2. Middle '0's sequence
        for (int i = 0; i < E - 4; i++) {
            cudaGraphLaunch(execSquare, stream);
        }

        // 3. Lower 4 bits "1010"
        cudaGraphLaunch(execSquareMul2, stream); // Bit '1'
        cudaGraphLaunch(execSquare, stream);     // Bit '0'
        cudaGraphLaunch(execSquareMul2, stream); // Bit '1'
        cudaGraphLaunch(execSquare, stream);     // Bit '0'

        // Synchronize and copy back to CPU
        cudaStreamSynchronize(stream);
        cudaMemcpy(data, d_data, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToHost);

        // Cleanup
        cudaGraphExecDestroy(execSquare);
        cudaGraphExecDestroy(execSquareMul2);
        cudaGraphDestroy(graphSquare);
        cudaGraphDestroy(graphSquareMul2);
        cudaStreamDestroy(stream);
        cudaFree(d_nextData);
        cudaFree(d_carryBuffer);
        
        return true;
    }
}
