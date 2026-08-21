#ifndef KASSYI_MATH_GPUBIGINT_CU_H
#define KASSYI_MATH_GPUBIGINT_CU_H

#ifdef _WIN32
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API
#endif

extern "C" {
    // Context Management
    EXPORT_API bool GpuBigInt_Init(int maxWords);
    EXPORT_API void GpuBigInt_Cleanup();
    
    // Memory Management
    EXPORT_API unsigned int* GpuBigInt_Alloc(int numWords);
    EXPORT_API void GpuBigInt_Free(unsigned int* ptr);
    EXPORT_API bool GpuBigInt_CopyHostToDevice(unsigned int* d_dst, const unsigned int* h_src, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_CopyDeviceToHost(unsigned int* h_dst, const unsigned int* d_src, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_CopyDeviceToDevice(unsigned int* d_dst, const unsigned int* d_src, int numWords, cudaStream_t stream = 0);

    // Core FFT Arithmetics
    EXPORT_API bool GpuBigInt_FermatPRPTest(int E, unsigned int* out_data);
    
    // Basic arithmetic and bitwise operations
    EXPORT_API bool GpuBigInt_Square(unsigned int* data, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_Add(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_Sub(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_ShiftLeft(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_ShiftRight(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    EXPORT_API bool GpuBigInt_BitwiseAnd(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cudaStream_t stream = 0);
    // Phase C: Intermediate Language / CUDA Graph execution
    EXPORT_API bool GpuBigInt_ExecuteIL(int E, const char* ilScript, unsigned int* out_data);
}

#endif
