#include <cuda_runtime.h>
#include <math_constants.h>

// Native CUDA reference implementation for Black-Scholes benchmark
extern "C" __global__ void BlackScholesKernel_Native(float* s, float* x, float* t, float r, float v, float* callResult, float* putResult, int n)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= n) return;

    float s_val = s[tid];
    float x_val = x[tid];
    float t_val = t[tid];

    float d1 = (logf(s_val / x_val) + (r + v * v / 2.0f) * t_val) / (v * sqrtf(t_val));
    float d2 = d1 - v * sqrtf(t_val);

    // CND approximation inline for Native CUDA
    auto cnd = [](float d) {
        float K = 1.0f / (1.0f + 0.2316419f * fabsf(d));
        float cnd_val = 0.3989422804f * expf(-0.5f * d * d) * 
                        (K * (0.31938153f + K * (-0.356563782f + K * (1.781477937f + K * (-1.821255978f + K * 1.330274429f)))));
        if (d > 0) cnd_val = 1.0f - cnd_val;
        return cnd_val;
    };

    float cndD1 = cnd(d1);
    float cndD2 = cnd(d2);

    float expRT = expf(-r * t_val);
    
    callResult[tid] = s_val * cndD1 - x_val * expRT * cndD2;
    putResult[tid] = x_val * expRT * (1.0f - cndD2) - s_val * (1.0f - cndD1);
}
