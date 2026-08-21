#pragma once
#include "GpuBigInt_Core.cuh"
#include "GpuBigInt_Kernels.cuh"

// -------------------------------------------------------------------------
// Host-Callable Math Wrappers
// -------------------------------------------------------------------------

bool MultiplySquare_Internal(unsigned int* data, int numWords, cudaStream_t stream = 0) {
    if (!d_real || !d_complex || !d_data) return false;
    
    int blockSize = 256;
    int gridSize = (g_M + blockSize - 1) / blockSize;
    
    UnpackKernel16<<<gridSize, blockSize, 0, stream>>>(data, d_real, g_M, numWords);
    
    cufftResult res1 = cufftExecD2Z(planR2C, d_real, d_complex);
    if (res1 != CUFFT_SUCCESS) printf("cufftExecD2Z failed: %d\n", res1);
    
    int complexGrid = (g_ComplexM + blockSize - 1) / blockSize;
    double scale = 1.0 / g_M;
    PointwiseSquareKernel<<<complexGrid, blockSize, 0, stream>>>(d_complex, g_ComplexM, scale);
    
    cufftResult res2 = cufftExecZ2D(planC2R, d_complex, d_real);
    if (res2 != CUFFT_SUCCESS) printf("cufftExecZ2D failed: %d\n", res2);
    
    int outNumWords = numWords * 2;
    cudaMemsetAsync(data, 0, sizeof(unsigned int) * outNumWords, stream);
    
    ResolveCarryAndPackKernel16<<<1, 1, 0, stream>>>(d_real, data, g_M, outNumWords);
    
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
