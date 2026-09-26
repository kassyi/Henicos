#ifndef KASSYI_MATH_GPUBIGINT_CU_H
#define KASSYI_MATH_GPUBIGINT_CU_H

#ifdef _WIN32
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API
#endif

extern "C" {
    // Context Management
    EXPORT_API void* GpuBigInt_Init(int maxWords);
    EXPORT_API void GpuBigInt_Cleanup(void* workspace);
    
    // Memory Management
    EXPORT_API unsigned int* GpuBigInt_Alloc(int numWords);
    EXPORT_API void GpuBigInt_Free(unsigned int* ptr);
    EXPORT_API bool GpuBigInt_CopyHostToDevice(unsigned int* d_dst, const unsigned int* h_src, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_CopyDeviceToHost(unsigned int* h_dst, const unsigned int* d_src, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_CopyDeviceToDevice(unsigned int* d_dst, const unsigned int* d_src, int numWords, cudaStream_t stream = 0);

    // Core FFT
    // Dynamic Fermat Loop using JIT
    EXPORT_API bool GpuBigInt_DynamicFermatLoop(void* workspace, int E, unsigned int* out_data, void(*jitLaunch)(unsigned int*, unsigned int*, unsigned int*, unsigned int*, unsigned int*, int, int, long long*, int, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, int, int));

    // Fermat PRP specific Loop
    EXPORT_API bool GpuBigInt_StaticFermatLoop(void* workspace, int E, unsigned int* out_data);
    EXPORT_API bool GpuBigInt_GraphFermatLoop(void* workspace, int E, unsigned int* out_data);
    
    // Basic arithmetic and bitwise operations
    // Note on Multiplication (Out-of-Core Architecture):
    // A monolithic full-multiplication mega-kernel is intentionally NOT provided in native CUDA.
    // In order to process numbers exceeding physical GPU VRAM, multiplication is orchestrated by the
    // host (C# AST / Out-of-Core streaming) using granular accelerated building blocks (Square, Add, Sub, NTT).
    EXPORT_API bool GpuBigInt_Square(void* workspace, unsigned int* data, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_Add(void* workspace, const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_Sub(void* workspace, const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_ShiftLeft(void* workspace, const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_ShiftRight(void* workspace, const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_BitwiseAnd(void* workspace, const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    // Phase C: Intermediate Language / CUDA Graph execution
    EXPORT_API bool GpuBigInt_ExecuteIL(void* workspace, int E, const char* ilScript, unsigned int* out_data);

    // ---------------------------------------------
    // GpuBigFloat / GpuUBigFloat Operations
    // ---------------------------------------------
    EXPORT_API bool GpuBigFloat_DivideByUInt32(void* workspace, const unsigned int* d_a, unsigned int* d_result, int numWords, unsigned int divisor, unsigned int* d_remainder, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigFloat_Normalize(void* workspace, const unsigned int* d_a, int numWordsA, unsigned int* d_result, int numWordsResult, int valueExp, int* h_resultExp, int* h_isZero, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigFloat_Reciprocal(void* workspace, const unsigned int* d_a, unsigned int* d_result, int numWords, int marginWords, cudaStream_t stream = 0);
}

#endif
