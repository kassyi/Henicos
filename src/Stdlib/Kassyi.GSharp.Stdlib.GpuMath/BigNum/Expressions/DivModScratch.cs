namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The global work words of a vector division whose elements are wider than the shared buffers of its kernel
/// (<see cref="GpuBigIntVectorKernels.DivModSharedWords"/> words for the remainder and as many for the quotient).
/// </summary>
internal static class DivModScratch
{
    /// <summary>
    /// Allocates <c>2 * evalWords</c> words per block, <c>evalWords</c> being the widest of the result and the two
    /// operands, or nothing when that fits in shared memory. The caller disposes it after the launch.
    /// </summary>
    public static GpuBigUInt? Rent(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation op,
        uint blocks)
    {
        var evalWords = Math.Max(op.Result.NumWords, Math.Max(op.Left.NumWords, op.Right.NumWords));
        return evalWords > GpuBigIntVectorKernels.DivModSharedWords
            ? new GpuBigUInt(workspace, checked((int)blocks * 2 * evalWords))
            : null;
    }
}
