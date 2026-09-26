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

// -------------------------------------------------------------------------
// Phase 2: Reciprocal (Newton-Raphson)
// -------------------------------------------------------------------------

__device__ inline void DeviceBigIntMul(const unsigned int* a, int lenA,
                                       const unsigned int* b, int lenB,
                                       unsigned int* out, int lenOut)
{
    for (int i = 0; i < lenOut; i++) out[i] = 0;
    
    for (int i = 0; i < lenA; i++) {
        if (a[i] == 0) continue;
        unsigned long long carry = 0;
        for (int j = 0; j < lenB; j++) {
            int k = i + j;
            if (k >= lenOut) break;
            unsigned long long sum = (unsigned long long)a[i] * b[j] + out[k] + carry;
            out[k] = (unsigned int)sum;
            carry = sum >> 32;
        }
        for (int k = i + lenB; k < lenOut && carry > 0; k++) {
            unsigned long long sum = (unsigned long long)out[k] + carry;
            out[k] = (unsigned int)sum;
            carry = sum >> 32;
        }
    }
}

/**
 * @brief Computes reciprocal approximation (1 / A) of a normalized BigFloat significand via Newton-Raphson iterations.
 */
__global__ void ReciprocalKernel(const unsigned int* __restrict__ d_a,
                                 unsigned int* __restrict__ d_result,
                                 int numWords,
                                 int marginWords)
{
    if (threadIdx.x == 0 && blockIdx.x == 0)
    {
        int actualBits = 0;
        for (int i = numWords - 1; i >= 0; i--) {
            if (d_a[i] != 0) {
                actualBits = i * 32 + (32 - __clz(d_a[i]));
                break;
            }
        }
        if (actualBits == 0) return;

        int destWords = 2 * numWords + marginWords;
        for (int i = 0; i < destWords; i++) d_result[i] = 0;

        int msWord = (actualBits - 1) / 32;
        unsigned long long topBig = d_a[msWord];
        if (msWord > 0) {
            topBig = (topBig << 32) | d_a[msWord - 1];
        }

        int topBits = 64 - __clzll(topBig);
        int shiftInTruncated = topBits - 62;
        unsigned long long originalBigTop = (shiftInTruncated >= 0) ? (topBig >> shiftInTruncated) : (topBig << (-shiftInTruncated));

        unsigned __int128 one124 = ((unsigned __int128)1) << 124;
        unsigned __int128 approx128 = one124 / originalBigTop;

        int numBits = numWords * 32;
        int marginBits = marginWords * 32;
        int finalShift = 2 * numBits + marginBits - actualBits - 62;

        if (finalShift >= 0) {
            int wordShift = finalShift / 32;
            int bitShift = finalShift % 32;
            unsigned int a0 = (unsigned int)approx128;
            unsigned int a1 = (unsigned int)(approx128 >> 32);
            unsigned int a2 = (unsigned int)(approx128 >> 64);

            unsigned long long s0 = (unsigned long long)a0 << bitShift;
            unsigned long long s1 = ((unsigned long long)a1 << bitShift) | (s0 >> 32);
            unsigned long long s2 = ((unsigned long long)a2 << bitShift) | (s1 >> 32);

            if (wordShift < destWords) d_result[wordShift] = (unsigned int)s0;
            if (wordShift + 1 < destWords) d_result[wordShift + 1] = (unsigned int)s1;
            if (wordShift + 2 < destWords) d_result[wordShift + 2] = (unsigned int)s2;
            if (wordShift + 3 < destWords) d_result[wordShift + 3] = (unsigned int)(s2 >> 32);
        }

        int targetBits = 2 * numBits + marginBits;
        int currentBits = 63;
        int iterations = 0;
        while (currentBits < targetBits) {
            iterations++;
            currentBits *= 2;
        }

        const int MAX_PROD = 256;
        const int MAX_NEXT = 512;
        unsigned int s_prod[MAX_PROD];
        unsigned int s_diff[MAX_PROD];
        unsigned int s_next[MAX_NEXT];

        int n = numWords;
        int m = marginWords;
        int prodWords = 3 * n + m;
        int nextWords = 5 * n + 2 * m;
        int shiftTotal = 2 * numBits + marginBits;
        int targetBit = 2 * numBits + marginBits + 1;

        if (prodWords <= MAX_PROD && nextWords <= MAX_NEXT) {
            for (int iter = 0; iter < iterations; iter++) {
                // 1. currentProduct = d_a * d_result
                DeviceBigIntMul(d_a, n, d_result, 2 * n + m, s_prod, prodWords);

                // 2. diff = constant - currentProduct
                unsigned long long borrow = 0;
                for (int i = 0; i < prodWords; i++) {
                    unsigned int c_word = 0;
                    if (i == targetBit / 32) {
                        c_word = (1U << (targetBit % 32));
                    }
                    unsigned long long sub = (unsigned long long)c_word - s_prod[i] - borrow;
                    s_diff[i] = (unsigned int)sub;
                    borrow = (sub >> 63) & 1;
                }

                // 3. nextIter = d_result * diff
                DeviceBigIntMul(d_result, 2 * n + m, s_diff, prodWords, s_next, nextWords);

                // 4. d_result = nextIter >> shiftTotal
                int wordShift = shiftTotal / 32;
                int bitShift = shiftTotal % 32;
                for (int i = 0; i < 2 * n + m; i++) {
                    int srcIdx = i + wordShift;
                    unsigned int w0 = (srcIdx < nextWords) ? s_next[srcIdx] : 0;
                    unsigned int w1 = (srcIdx + 1 < nextWords) ? s_next[srcIdx + 1] : 0;
                    d_result[i] = (w0 >> bitShift) | (bitShift == 0 ? 0 : (w1 << (32 - bitShift)));
                }
            }
        }
    }
}

inline bool LaunchReciprocal(const unsigned int* d_a,
                             unsigned int* d_result,
                             int numWords,
                             int marginWords,
                             cudaStream_t stream)
{
    dim3 grid(1);
    dim3 block(32);
    ReciprocalKernel<<<grid, block, 0, stream>>>(d_a, d_result, numWords, marginWords);
    cudaError_t err = cudaGetLastError();
    return err == cudaSuccess;
}

#endif // GPUBIGFLOAT_KERNELS_CUH
