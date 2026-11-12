namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal static class VectorCompareLaunch
{
    // A scalar side: its own word count and sign, read for every element (stride 0)
    public static GpuBigIntCmpKernels.AotContexts.SignedCompareOperand Scalar(GpuBigInt scalar) => new()
    {
        Limbs = scalar.Value.ToProxy(),
        Signs = default,
        NumWords = scalar.Value.NumWords,
        Stride = 0,
        ScalarSign = scalar.IsNegative ? 1u : 0u
    };

    public static GpuBigIntCmpKernels.AotContexts.SignedCompareOperand Operand(GpuBigIntArrayView vector) => new()
    {
        Limbs = vector.Values.ToProxy(),
        Signs = vector.Signs.ToProxy(),
        NumWords = vector.WordsPerElement,
        Stride = vector.WordsPerElement,
        ScalarSign = 0u
    };

    // One block per element, as wide as the wider side rounded up to whole warps (at most 1024 threads), so that the
    // block-wide reductions of the kernel never see a partial warp; the extra lanes read no word. An empty vector
    // launches nothing: a grid of 0 blocks is an invalid launch.
    public static void Launch(
        GpuWorkspace workspace,
        GpuBigIntCmpKernels.AotContexts.VectorCompare ctx,
        int length,
        int leftWords,
        int rightWords)
    {
        if (length == 0)
        {
            return;
        }

        var words = Math.Max(leftWords, rightWords);
        var blockDimX = Math.Min(1024, (words + 31) & ~31);
        GpuBigIntCmpKernels.LaunchVectorCompareKernelPtr(
            ctx,
            (uint)length, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);
    }
}
