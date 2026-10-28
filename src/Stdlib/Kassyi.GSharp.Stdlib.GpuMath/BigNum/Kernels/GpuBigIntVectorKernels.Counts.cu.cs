namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    /// <summary>
    /// Writes 1 into <c>Result[0]</c> when one of the first n flags is not 0 (n from <c>Count</c>). The caller clears
    /// <c>Result[0]</c> first, so an empty vector gives false, as System.Linq's <c>Any</c>.
    /// </summary>
    [GpuKernel]
    public static void VectorAnyTrueKernel(VectorBoolReductionContext ctx)
    {
        int n = ResolveElementCount(ctx.Count);
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int stride = GridDimension.X * BlockDimension.X;
        for (; i < n; i += stride)
        {
            if (ctx.Flags[i] != 0u)
            {
                Gpu.AtomicMax(ref ctx.Result[0], 1u);
            }
        }
    }

    /// <summary>
    /// Writes 0 into <c>Result[0]</c> when one of the first n flags is 0. The caller sets <c>Result[0]</c> to 1 first,
    /// so an empty vector gives true, as System.Linq's <c>All</c>.
    /// </summary>
    [GpuKernel]
    public static void VectorAllTrueKernel(VectorBoolReductionContext ctx)
    {
        int n = ResolveElementCount(ctx.Count);
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int stride = GridDimension.X * BlockDimension.X;
        for (; i < n; i += stride)
        {
            if (ctx.Flags[i] == 0u)
            {
                Gpu.AtomicExch(ref ctx.Result[0], 0u);
            }
        }
    }

    /// <summary>
    /// Writes the element count n into <c>Destination[0]</c> and clears the other words, without a host
    /// synchronization (the count of a vector after <c>Where</c> is known only on the device).
    /// </summary>
    [GpuKernel]
    public static void WriteElementCountKernel(ElementCountWriteContext ctx)
    {
        int n = ResolveElementCount(ctx.Count);
        for (int w = ThreadIndex.X; w < ctx.DestinationWords; w += BlockDimension.X)
        {
            ctx.Destination[w] = w == 0 ? (uint)n : 0u;
        }
    }
}
