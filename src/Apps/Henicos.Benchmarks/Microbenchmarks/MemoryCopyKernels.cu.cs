namespace Henicos.Benchmarks.Microbenchmarks;

public static class MemoryCopyKernels
{
    [GpuKernel]
    public static void MemoryCopyKernel(float[] src, float[] dst, int n)
    {
        int idx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (idx < n)
        {
            dst[idx] = src[idx];
        }
    }
}
