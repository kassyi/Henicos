#include <cuda_runtime.h>

// Native CUDA reference implementation for parallel reduction benchmark
extern "C" __global__ void ReductionKernel_Native(float4* input, float* output, int n)
{
    __shared__ float sdata[256];

    int tid = threadIdx.x;
    int i = blockIdx.x * (blockDim.x * 2) + threadIdx.x;

    // Perform first level of reduction: read two elements into shared memory
    float mySum = 0;
    if (i < n)
    {
        float4 v = input[i];
        mySum = v.x + v.y + v.z + v.w;
    }
    if (i + blockDim.x < n)
    {
        float4 v = input[i + blockDim.x];
        mySum += v.x + v.y + v.z + v.w;
    }
    
    sdata[tid] = mySum;
    __syncthreads();

    // Perform reduction in shared memory
    for (int s = blockDim.x / 2; s > 0; s >>= 1)
    {
        if (tid < s)
        {
            sdata[tid] += sdata[tid + s];
        }
        __syncthreads();
    }

    // Write result for this block to global mem
    if (tid == 0)
    {
        output[blockIdx.x] = sdata[0];
    }
}
