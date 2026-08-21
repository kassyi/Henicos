#pragma once
#include "GpuBigInt_Core.cuh"
#include "GpuBigInt_Kernels.cuh"

// -------------------------------------------------------------------------
// Host-Callable Math Wrappers
// -------------------------------------------------------------------------

bool MultiplySquare_Internal(unsigned int* data, int numWords, cudaStream_t stream = 0) {
    if (!d_ntt_buf1 || !d_ntt_buf2 || !d_data) return false;
    
    int blockSize = 256;
    
    // Step 1: Unpack and Bit Reverse
    int log2N = 0;
    while ((1 << log2N) < g_M) log2N++;
    
    int unpackGrid = (g_M + blockSize - 1) / blockSize;
    Kernel_UnpackAndBitReverse16<<<unpackGrid, blockSize, 0, stream>>>(data, d_ntt_buf1, d_ntt_buf2, g_M, log2N, numWords);
    
    // Step 2: Forward NTT for both buffers (Iterative Cooley-Tukey)
    for (int len = 2; len <= g_M; len <<= 1) {
        int stageGrid = ((g_M / 2) + blockSize - 1) / blockSize;
        Kernel_NTT_Stage<<<stageGrid, blockSize, 0, stream>>>(d_ntt_buf1, d_twiddle1_fwd, g_M, len, NTT_P1, NTT_M1);
        Kernel_NTT_Stage<<<stageGrid, blockSize, 0, stream>>>(d_ntt_buf2, d_twiddle2_fwd, g_M, len, NTT_P2, NTT_M2);
    }
    
    // Step 3: Pointwise Square
    int pointGrid = (g_M + blockSize - 1) / blockSize;
    Kernel_NTT_PointwiseSquare<<<pointGrid, blockSize, 0, stream>>>(d_ntt_buf1, d_ntt_buf2, g_M);
    
    // Step 4: Bit Reverse for Inverse NTT
    Kernel_BitReverse<<<unpackGrid, blockSize, 0, stream>>>(d_ntt_buf1, d_ntt_tmp, g_M, log2N);
    cudaMemcpyAsync(d_ntt_buf1, d_ntt_tmp, sizeof(uint32_t) * g_M, cudaMemcpyDeviceToDevice, stream);
    
    Kernel_BitReverse<<<unpackGrid, blockSize, 0, stream>>>(d_ntt_buf2, d_ntt_tmp, g_M, log2N);
    cudaMemcpyAsync(d_ntt_buf2, d_ntt_tmp, sizeof(uint32_t) * g_M, cudaMemcpyDeviceToDevice, stream);
    
    // Step 5: Inverse NTT
    for (int len = 2; len <= g_M; len <<= 1) {
        int stageGrid = ((g_M / 2) + blockSize - 1) / blockSize;
        Kernel_NTT_Stage<<<stageGrid, blockSize, 0, stream>>>(d_ntt_buf1, d_twiddle1_inv, g_M, len, NTT_P1, NTT_M1);
        Kernel_NTT_Stage<<<stageGrid, blockSize, 0, stream>>>(d_ntt_buf2, d_twiddle2_inv, g_M, len, NTT_P2, NTT_M2);
    }
    
    // Step 6: CRT Resolve and Pack
    int outNumWords = numWords * 2;
    cudaMemsetAsync(data, 0, sizeof(unsigned int) * outNumWords, stream);
    Kernel_CRT_ResolveCarry<<<1, 1, 0, stream>>>(d_ntt_buf1, d_ntt_buf2, data, g_M, outNumWords);
    
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
