#pragma once
#include "GpuBigInt_Core.cuh"
#include "GpuBigInt_Kernels.cuh"

// -------------------------------------------------------------------------
// Host-Callable Math Wrappers
// -------------------------------------------------------------------------

bool MultiplySquare_Internal(unsigned int* data, int numWords, cudaStream_t stream = 0) {
    if (!d_ntt_buf1 || !d_ntt_buf2 || !d_data) return false;
    
    int blockSize = 256;
    
    // Step 1: Unpack
    int unpackGrid = (g_M + blockSize - 1) / blockSize;
    Kernel_Unpack16<<<unpackGrid, blockSize, 0, stream>>>(data, d_ntt_buf1, d_ntt_buf2, g_M, numWords);
    
    // Six-Step FFT Configuration
    int N2 = 1;
    while (N2 * N2 < g_M) N2 *= 2;
    int N1 = g_M / N2;
    
    dim3 blockT(32, 32);
    dim3 gridT1((N2 + 31) / 32, (N1 + 31) / 32);
    dim3 gridT2((N1 + 31) / 32, (N2 + 31) / 32);
    int sharedSize1 = N2 * sizeof(uint32_t);
    int sharedSize2 = N1 * sizeof(uint32_t);

    auto do_forward_ntt = [&](uint32_t* buf, const uint32_t* twiddle, uint32_t p, uint64_t m) {
        if (g_M <= 2048) {
            Kernel_NTT_Shared_Row<<<1, g_M / 2, g_M * sizeof(uint32_t), stream>>>(buf, twiddle, g_M, g_M, p, m, false, false);
            return;
        }
        // 1. Transpose N1xN2 -> N2xN1
        Kernel_Transpose_32x32<<<gridT1, blockT, 0, stream>>>(buf, d_ntt_tmp, N2, N1);
        // 2. Row NTT (length N1) with twiddle
        Kernel_NTT_Shared_Row<<<N2, N1 / 2, sharedSize2, stream>>>(d_ntt_tmp, twiddle, g_M, N1, p, m, false, true);
        // 3. Transpose N2xN1 -> N1xN2
        Kernel_Transpose_32x32<<<gridT2, blockT, 0, stream>>>(d_ntt_tmp, buf, N1, N2);
        // 4. Row NTT (length N2)
        Kernel_NTT_Shared_Row<<<N1, N2 / 2, sharedSize1, stream>>>(buf, twiddle, g_M, N2, p, m, false, false);
    };

    // Step 2: Forward NTT for both buffers
    do_forward_ntt(d_ntt_buf1, d_twiddle1_fwd, NTT_P1, NTT_M1);
    do_forward_ntt(d_ntt_buf2, d_twiddle2_fwd, NTT_P2, NTT_M2);
    
    // Step 3: Pointwise Square
    int pointGrid = (g_M + blockSize - 1) / blockSize;
    Kernel_NTT_PointwiseSquare<<<pointGrid, blockSize, 0, stream>>>(d_ntt_buf1, d_ntt_buf2, g_M);
    
    auto do_inverse_ntt = [&](uint32_t* buf, const uint32_t* twiddle, uint32_t p, uint64_t m) {
        if (g_M <= 2048) {
            Kernel_NTT_Shared_Row<<<1, g_M / 2, g_M * sizeof(uint32_t), stream>>>(buf, twiddle, g_M, g_M, p, m, false, false);
            return;
        }
        // 1. Row INTT (length N2) with twiddle
        Kernel_NTT_Shared_Row<<<N1, N2 / 2, sharedSize1, stream>>>(buf, twiddle, g_M, N2, p, m, false, true);
        // 2. Transpose N1xN2 -> N2xN1
        Kernel_Transpose_32x32<<<gridT1, blockT, 0, stream>>>(buf, d_ntt_tmp, N2, N1);
        // 3. Row INTT (length N1)
        Kernel_NTT_Shared_Row<<<N2, N1 / 2, sharedSize2, stream>>>(d_ntt_tmp, twiddle, g_M, N1, p, m, false, false);
        // 4. Transpose N2xN1 -> N1xN2
        Kernel_Transpose_32x32<<<gridT2, blockT, 0, stream>>>(d_ntt_tmp, buf, N1, N2);
    };

    // Step 4 & 5: Inverse NTT
    do_inverse_ntt(d_ntt_buf1, d_twiddle1_inv, NTT_P1, NTT_M1);
    do_inverse_ntt(d_ntt_buf2, d_twiddle2_inv, NTT_P2, NTT_M2);
    
    // Step 6: CRT Resolve and Pack
    int outNumWords = numWords * 2;
    cudaMemsetAsync(data, 0, sizeof(unsigned int) * outNumWords, stream);
    Kernel_CRT_ParallelAtomic<<<unpackGrid, blockSize, 0, stream>>>(d_ntt_buf1, d_ntt_buf2, data, g_M, outNumWords);
    
    return true;
}

extern "C" {

    EXPORT_API bool GpuBigInt_Square(unsigned int* data, int numWords, cudaStream_t stream) { 
        return MultiplySquare_Internal(data, numWords, stream); 
    }

    EXPORT_API bool GpuBigInt_Add(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int chunkSize = 256;
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

    EXPORT_API bool GpuBigInt_BitwiseAnd(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream) {
        int blockSize = 256;
        int gridSize = (numWords + blockSize - 1) / blockSize;
        if (gridSize == 0) gridSize = 1;
        BitwiseAndKernel<<<gridSize, blockSize, 0, stream>>>(d_a, d_b, d_result, numWords);
        return true;
    }

}
