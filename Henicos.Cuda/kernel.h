#ifndef HENICOS_CUDA_KERNEL_H
#define HENICOS_CUDA_KERNEL_H

#ifdef _WIN32
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API
#endif

extern "C" {
    EXPORT_API bool InitializeGPU(int E);
    EXPORT_API bool MultiplySquare(unsigned int* data, int numWords);
    EXPORT_API bool RunFermatLoopV5(unsigned int* data, int numWords, int E);
    EXPORT_API void CleanupGPU();
}

#endif
