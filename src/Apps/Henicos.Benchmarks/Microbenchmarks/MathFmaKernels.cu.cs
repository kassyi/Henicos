namespace Henicos.Benchmarks.Microbenchmarks;

public static class MathFmaKernels
{
    [GpuKernel]
    public static void MathFmaKernel(float[] data, int n, int iterations)
    {
        int idx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
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
}
