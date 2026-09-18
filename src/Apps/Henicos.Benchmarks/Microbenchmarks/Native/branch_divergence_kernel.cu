extern "C" __global__ void BranchDivergenceKernel_Native(float* data, int n)
{
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx < n)
    {
        float val = data[idx];
        if (idx % 2 == 0)
        {
            for (int i = 0; i < 100; i++)
            {
                val = val * 1.01f + 0.01f;
            }
        }
        else
        {
            for (int i = 0; i < 100; i++)
            {
                val = val * 0.99f - 0.01f;
            }
        }
        data[idx] = val;
    }
}
