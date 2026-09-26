#ifndef GPUBIGFLOAT_KERNELS_CUH
#define GPUBIGFLOAT_KERNELS_CUH

#include <cuda_runtime.h>
#include <stdint.h>
#include "GpuBigInt_Core.cuh"

// -------------------------------------------------------------------------
// GpuBigFloat / GpuBigUFloat Native Kernels
// Phase 1: Scalar Division
// -------------------------------------------------------------------------

/**
 * @brief Performs scalar division of a multi-word BigInt (UFloat significand) by a 32-bit unsigned integer.
 * This kernel uses a Warp-Master Serial pattern or purely single-thread execution for a single BigInt,
 * since scalar division has a strict MSD-to-LSD serial dependency.
 */
__global__ void DivideByUInt32Kernel(const unsigned int* __restrict__ d_a,
                                     unsigned int* __restrict__ d_result,
                                     int numWords,
                                     unsigned int divisor,
                                     unsigned int* __restrict__ d_remainder)
{
    // For a single BigFloat instance, only thread 0 does the work.
    // In a batched scenario, threadIdx.x would index the BigFloat array.
    if (threadIdx.x == 0 && blockIdx.x == 0)
    {
        unsigned long long current_rem = 0;
        // BigInts are assumed Little-Endian: index 0 is least significant word, numWords-1 is most significant.
        for (int i = numWords - 1; i >= 0; i--)
        {
            // Shift remainder by 32 bits and add the current word
            current_rem = (current_rem << 32) | d_a[i];
            
            // Divide
            d_result[i] = static_cast<unsigned int>(current_rem / divisor);
            current_rem = current_rem % divisor;
        }

        // Write remainder if requested
        if (d_remainder != nullptr)
        {
            *d_remainder = static_cast<unsigned int>(current_rem);
        }
    }
}

// -------------------------------------------------------------------------
// Host Launchers
// -------------------------------------------------------------------------

inline bool LaunchDivideByUInt32(const unsigned int* d_a,
                                 unsigned int* d_result,
                                 int numWords,
                                 unsigned int divisor,
                                 unsigned int* h_remainder, // Now expects a host pointer
                                 cudaStream_t stream)
{
    if (divisor == 0) return false; // Divide by zero protection

    unsigned int* d_rem = nullptr;
    if (h_remainder != nullptr) {
        cudaMallocAsync(&d_rem, sizeof(unsigned int), stream);
    }

    // Launch with 1 block of 32 threads (1 Warp). 
    dim3 grid(1);
    dim3 block(32);

    DivideByUInt32Kernel<<<grid, block, 0, stream>>>(d_a, d_result, numWords, divisor, d_rem);
    
    if (h_remainder != nullptr && d_rem != nullptr) {
        cudaMemcpyAsync(h_remainder, d_rem, sizeof(unsigned int), cudaMemcpyDeviceToHost, stream);
        cudaStreamSynchronize(stream);
        cudaFreeAsync(d_rem, stream);
    }

    cudaError_t err = cudaGetLastError();
    return err == cudaSuccess;
}

// -------------------------------------------------------------------------
// Phase 2: Basic Arithmetic (Normalize, Add, Sub, Mul)
// -------------------------------------------------------------------------

/**
 * @brief Normalizes a BigFloat significand to align its most significant bit to the highest bit of the target array.
 */
__global__ void NormalizeKernel(const unsigned int* __restrict__ d_a,
                                int numWordsA,
                                unsigned int* __restrict__ d_result,
                                int numWordsResult,
                                int valueExp,
                                int* __restrict__ d_resultExp,
                                int* __restrict__ d_isZero)
{
    __shared__ int shared_currentBitLength;
    __shared__ int shared_shift;

    if (threadIdx.x == 0 && blockIdx.x == 0)
    {
        int bitLen = 0;
        for (int i = numWordsA - 1; i >= 0; i--)
        {
            unsigned int w = d_a[i];
            if (w != 0)
            {
                int clz = __clz(w); // 0 to 31
                bitLen = i * 32 + (32 - clz);
                break;
            }
        }
        shared_currentBitLength = bitLen;
        
        if (bitLen == 0) {
            shared_shift = 0;
            if (d_resultExp != nullptr) *d_resultExp = 0;
            if (d_isZero != nullptr) *d_isZero = 1;
        } else {
            int targetBitLength = numWordsResult * 32;
            int shift = targetBitLength - bitLen;
            shared_shift = shift;
            if (d_resultExp != nullptr) *d_resultExp = valueExp - shift;
            if (d_isZero != nullptr) *d_isZero = 0;
        }
    }

    __syncthreads();

    int bitLen = shared_currentBitLength;
    if (bitLen == 0) {
        for (int i = threadIdx.x; i < numWordsResult; i += blockDim.x)
        {
            d_result[i] = 0;
        }
        return;
    }

    int shift = shared_shift;
    int wordShift = shift / 32;
    int bitShift = shift % 32;
    if (bitShift < 0) {
        wordShift--;
        bitShift += 32;
    }

    for (int i = threadIdx.x; i < numWordsResult; i += blockDim.x)
    {
        int srcIdx = i - wordShift;
        unsigned int val = 0;
        if (srcIdx >= 0 && srcIdx < numWordsA)
        {
            val = d_a[srcIdx] << bitShift;
        }
        if (bitShift > 0 && srcIdx - 1 >= 0 && srcIdx - 1 < numWordsA)
        {
            val |= (d_a[srcIdx - 1] >> (32 - bitShift));
        }
        d_result[i] = val;
    }
}

__global__ void CompareToKernel(const unsigned int* __restrict__ d_a, const unsigned int* __restrict__ d_b, int* __restrict__ d_result, int numWords)
{
    if (threadIdx.x == 0 && blockIdx.x == 0)
    {
        int cmp = 0;
        for (int i = numWords - 1; i >= 0; i--)
        {
            unsigned int a = d_a[i];
            unsigned int b = d_b[i];
            if (a > b) { cmp = 1; break; }
            if (a < b) { cmp = -1; break; }
        }
        *d_result = cmp;
    }
}

inline bool LaunchNormalize(const unsigned int* d_a,
                            int numWordsA,
                            unsigned int* d_result,
                            int numWordsResult,
                            int valueExp,
                            int* h_resultExp,
                            int* h_isZero,
                            int* d_exp,
                            int* d_zero,
                            cudaStream_t stream)
{
    dim3 grid(1);
    dim3 block(32);
    NormalizeKernel<<<grid, block, 0, stream>>>(d_a, numWordsA, d_result, numWordsResult, valueExp, d_exp, d_zero);

    if (h_resultExp != nullptr && d_exp != nullptr) cudaMemcpyAsync(h_resultExp, d_exp, sizeof(int), cudaMemcpyDeviceToHost, stream);
    if (h_isZero != nullptr && d_zero != nullptr) cudaMemcpyAsync(h_isZero, d_zero, sizeof(int), cudaMemcpyDeviceToHost, stream);
    
    cudaStreamSynchronize(stream);

    cudaError_t err = cudaGetLastError();
    return err == cudaSuccess;
}

#endif // GPUBIGFLOAT_KERNELS_CUH
