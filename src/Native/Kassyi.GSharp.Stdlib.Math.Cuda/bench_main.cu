#include <iostream>
#include <vector>
#include <chrono>
#include <cuda_runtime.h>

#define CHECK_CUDA(call) \
{ \
    cudaError_t err = call; \
    if (err != cudaSuccess) { \
        std::cerr << "CUDA error in " << __FILE__ << ":" << __LINE__ << ": " << cudaGetErrorString(err) << std::endl; \
        exit(EXIT_FAILURE); \
    } \
}

// 32-bit unsigned multiplication widening to 64-bit result (PTX mul.wide.u32).
__device__ __forceinline__ unsigned long long __gsharp_mul_wide(unsigned int a, unsigned int b) {
    unsigned long long res;
    asm volatile("mul.wide.u32 %0, %1, %2;" : "=l"(res) : "r"(a), "r"(b));
    return res;
}

// Original Barrett
__device__ __forceinline__ unsigned int ModMulBarrett(unsigned int operandA, unsigned int operandB, unsigned int primeModulus, unsigned long long barrettMagic) {
    auto p1 = __gsharp_mul_wide(operandA, operandB);
    auto q = (unsigned int)__umul64hi(p1, barrettMagic);
    auto p2 = __gsharp_mul_wide(q, primeModulus);
    auto rem = (unsigned int)p1 - (unsigned int)p2;
    if (rem >= primeModulus) {
        rem -= primeModulus;
    }
    return rem;
}

// Montgomery Reduction
// P_inv is -P^{-1} mod 2^32
__device__ __forceinline__ unsigned int ModMulMontgomery(unsigned int a, unsigned int b, unsigned int p, unsigned int p_inv) {
    unsigned long long T = __gsharp_mul_wide(a, b);
    unsigned int m = ((unsigned int)T) * p_inv;
    unsigned long long m_P = __gsharp_mul_wide(m, p);
    unsigned int t = (unsigned int)((T + m_P) >> 32);
    if (t >= p) t -= p;
    return t;
}

// Montgomery PTX
__device__ __forceinline__ unsigned int ModMulMontgomery_PTX(unsigned int a, unsigned int b, unsigned int p, unsigned int p_inv) {
    unsigned int t_lo, t_hi;
    asm volatile(
        "mul.lo.u32 %0, %2, %3;\n\t"
        "mul.hi.u32 %1, %2, %3;"
        : "=r"(t_lo), "=r"(t_hi) : "r"(a), "r"(b)
    );
    unsigned int m = t_lo * p_inv;
    unsigned int res_hi;
    unsigned int dummy;
    asm volatile(
        "mad.lo.cc.u32 %1, %2, %3, %4;\n\t"
        "madc.hi.u32 %0, %2, %3, %5;"
        : "=r"(res_hi), "=r"(dummy) : "r"(m), "r"(p), "r"(t_lo), "r"(t_hi)
    );
    if (res_hi >= p) res_hi -= p;
    return res_hi;
}

__global__ void Kernel_BenchmarkMul(unsigned int* dataA, unsigned int* dataB, unsigned int* dataOut, int n, unsigned int p = 3221225473u, unsigned long long pm = 13915155169623190ull) {
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx < n) {
        unsigned int a = dataA[idx];
        unsigned int b = dataB[idx];
        for (int i = 0; i < 1000; i++) {
            a = ModMulBarrett(a, b, p, pm);
        }
        dataOut[idx] = a;
    }
}

__global__ void Kernel_BenchmarkMont(unsigned int* dataA, unsigned int* dataB, unsigned int* dataOut, int n, unsigned int p = 3221225473u, unsigned int p_inv = 3221225471u) {
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx < n) {
        unsigned int a = dataA[idx];
        unsigned int b = dataB[idx];
        for (int i = 0; i < 1000; i++) {
            a = ModMulMontgomery(a, b, p, p_inv);
        }
        dataOut[idx] = a;
    }
}

__global__ void Kernel_BenchmarkMontPTX(unsigned int* dataA, unsigned int* dataB, unsigned int* dataOut, int n, unsigned int p = 3221225473u, unsigned int p_inv = 3221225471u) {
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx < n) {
        unsigned int a = dataA[idx];
        unsigned int b = dataB[idx];
        for (int i = 0; i < 1000; i++) {
            a = ModMulMontgomery_PTX(a, b, p, p_inv);
        }
        dataOut[idx] = a;
    }
}

// Precomputes -P^{-1} mod 2^32 using Newton-Raphson
unsigned int ComputeMontgomeryPInv(unsigned int p) {
    unsigned int inv = p; // Guess
    for (int i = 0; i < 4; i++) {
        inv = inv * (2u - p * inv);
    }
    return (unsigned int)(-(int)inv);
}

int main()
{
    int n = 1 << 24; // 16M elements
    unsigned int p = 3221225473u;
    unsigned long long pm = 13915155169623190ull; 
    unsigned int p_inv = ComputeMontgomeryPInv(p);

    size_t bytes = n * sizeof(unsigned int);

    unsigned int *d_A, *d_B, *d_Out;
    CHECK_CUDA(cudaMalloc(&d_A, bytes));
    CHECK_CUDA(cudaMalloc(&d_B, bytes));
    CHECK_CUDA(cudaMalloc(&d_Out, bytes));
    
    CHECK_CUDA(cudaMemset(d_A, 1234567, bytes));
    CHECK_CUDA(cudaMemset(d_B, 7654321, bytes));

    int blockDim = 1024;
    int gridDim = (n + blockDim - 1) / blockDim;
    
    // Warmup Barrett
    Kernel_BenchmarkMul<<<gridDim, blockDim>>>(d_A, d_B, d_Out, n);
    CHECK_CUDA(cudaDeviceSynchronize());

    int iterations = 10;
    cudaEvent_t start, stop;
    cudaEventCreate(&start);
    cudaEventCreate(&stop);

    cudaEventRecord(start);
    for (int i = 0; i < iterations; i++)
    {
        Kernel_BenchmarkMul<<<gridDim, blockDim>>>(d_A, d_B, d_Out, n);
    }
    cudaEventRecord(stop);
    cudaEventSynchronize(stop);
    float ms_barrett = 0;
    cudaEventElapsedTime(&ms_barrett, start, stop);

    // Warmup Montgomery
    Kernel_BenchmarkMont<<<gridDim, blockDim>>>(d_A, d_B, d_Out, n);
    CHECK_CUDA(cudaDeviceSynchronize());

    cudaEventRecord(start);
    for (int i = 0; i < iterations; i++)
    {
        Kernel_BenchmarkMont<<<gridDim, blockDim>>>(d_A, d_B, d_Out, n);
    }
    cudaEventRecord(stop);
    cudaEventSynchronize(stop);
    float ms_mont = 0;
    cudaEventElapsedTime(&ms_mont, start, stop);

    // Warmup Montgomery PTX
    Kernel_BenchmarkMontPTX<<<gridDim, blockDim>>>(d_A, d_B, d_Out, n);
    CHECK_CUDA(cudaDeviceSynchronize());

    cudaEventRecord(start);
    for (int i = 0; i < iterations; i++)
    {
        Kernel_BenchmarkMontPTX<<<gridDim, blockDim>>>(d_A, d_B, d_Out, n);
    }
    cudaEventRecord(stop);
    cudaEventSynchronize(stop);
    float ms_mont_ptx = 0;
    cudaEventElapsedTime(&ms_mont_ptx, start, stop);

    std::cout << "Original Barrett Time : " << (ms_barrett / iterations) << " ms" << std::endl;
    std::cout << "Montgomery Time       : " << (ms_mont / iterations) << " ms" << std::endl;
    std::cout << "Montgomery PTX Time   : " << (ms_mont_ptx / iterations) << " ms" << std::endl;

    cudaFree(d_A); cudaFree(d_B); cudaFree(d_Out);
    return 0;
}
