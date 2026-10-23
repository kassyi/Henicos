namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class MemoryCopyKernels
{
    [GpuKernel]
    public static void MemoryCopyKernel([In] float[] src, [Out] float[] dst, int n)
    {
        var idx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (idx < n)
        {
            dst[idx] = src[idx];
        }
    }
}
