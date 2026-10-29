// A hand-written kernel loaded as an external module through [CudaImport] (DynamicSharedMemoryModuleTests, #820).
// It uses n uints of dynamic shared memory: s[i] = i * 2654435761 + 12345, and output[t] is the sum of s[n - 1 - j] for
// j = t, t + blockDim, ..., so a region shorter than requested changes the sums.
extern "C" __global__ void DynamicSharedMemoryModuleKernel(unsigned int* output, int n)
{
    extern __shared__ unsigned int s[];
    int tid = threadIdx.x;
    for (int i = tid; i < n; i += blockDim.x)
    {
        s[i] = (unsigned int)i * 2654435761u + 12345u;
    }

    __syncthreads();
    unsigned int sum = 0u;
    for (int j = tid; j < n; j += blockDim.x)
    {
        sum += s[n - 1 - j];
    }

    output[tid] = sum;
}
