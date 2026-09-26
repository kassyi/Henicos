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

#endif // GPUBIGFLOAT_KERNELS_CUH
