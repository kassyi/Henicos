extern "C" __global__ void MathFmaKernel_Native(float* data, int n, int iterations)
{
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx < n)
    {
        float val = data[idx];
        float a = val;
        float b = 1.00001f;
        float c = 0.00001f;

        for (int i = 0; i < iterations; i++)
        {
            a = a * b + c;
        }

        data[idx] = a;
    }
}
