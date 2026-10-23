namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class BranchDivergenceKernels
{
    [GpuKernel]
    public static void BranchDivergenceKernel(float[] data, int n)
    {
        var idx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (idx < n)
        {
            var val = data[idx];
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
}
