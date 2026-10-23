namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class MathFmaKernels
{
    [GpuKernel]
    public static void MathFmaKernel(float[] data, int n, int iterations)
    {
        var idx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (idx < n)
        {
            var a = data[idx];
            var b = 1.00001f;
            var c = 0.00001f;

            for (int i = 0; i < iterations; i++)
            {
                a = a * b + c;
            }

            data[idx] = a;
        }
    }
}
