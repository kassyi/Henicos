#include <cuda_runtime.h>
#include <math_constants.h>

struct BlackScholesContext {
    float* __restrict__ S;
    float* __restrict__ X;
    float* __restrict__ T;
    float R;
    float V;
    float* __restrict__ CallResult;
    float* __restrict__ PutResult;
    int N;
};

// Native CUDA reference implementation for Black-Scholes benchmark
extern "C" __global__ void BlackScholesKernel_Native(BlackScholesContext ctx)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= ctx.N) return;
        float s_val = ctx.S[tid];
        float x_val = ctx.X[tid];
        float t_val = ctx.T[tid];

        float d1 = (__logf(s_val / x_val) + (ctx.R + ctx.V * ctx.V / 2.0f) * t_val) / (ctx.V * __fsqrt_rn(t_val));
        float d2 = d1 - ctx.V * __fsqrt_rn(t_val);

        // CND approximation inline for Native CUDA
        auto cnd = [](float d) {
            float K = 1.0f / (1.0f + 0.2316419f * fabsf(d));
            float cnd_val = 0.3989422804f * __expf(-0.5f * d * d) * 
                            (K * (0.31938153f + K * (-0.356563782f + K * (1.781477937f + K * (-1.821255978f + K * 1.330274429f)))));
            if (d > 0) cnd_val = 1.0f - cnd_val;
            return cnd_val;
        };

        float cndD1 = cnd(d1);
        float cndD2 = cnd(d2);

        float expRT = __expf(-ctx.R * t_val);
        
        ctx.CallResult[tid] = s_val * cndD1 - x_val * expRT * cndD2;
        ctx.PutResult[tid] = x_val * expRT * (1.0f - cndD2) - s_val * (1.0f - cndD1);
}
