extern "C" __global__ void MemoryCopyKernel_Native(const float* src, float* dst, int n)
{
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx < n)
    {
        dst[idx] = src[idx];
    }
}
