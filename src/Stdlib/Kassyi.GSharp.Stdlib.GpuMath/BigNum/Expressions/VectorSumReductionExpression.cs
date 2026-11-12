namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// The sum of up to Capacity elements of b bits has at most b + ceil(log2 Capacity) bits (.NET Enumerable.Sum of
// BigInteger cannot overflow either); the result has the words of that bound, which the accumulator (one word more
// than the elements) always holds, so the sum never overflows.
internal sealed class VectorSumReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : VectorReductionExpressionBase(vector)
{
    public override long Bits => Vector.BitsPerElement + CeilLog2(Vector.Capacity);

    // ceil(log2 n) for n >= 1; 0 for a capacity of 0 or 1
    private static int CeilLog2(int n) =>
        n <= 1 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(n - 1));

    protected override bool UseOffsetBinary => false;

    protected override bool EmptyIsError => false;

    protected override void DispatchChunkKernel(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorReductionChunk chunkCtx,
        uint gridDimX,
        uint blockDimX)
    {
        GpuBigIntVectorKernels.LaunchVectorSumReductionChunkKernelPtr(
            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
    }
}
