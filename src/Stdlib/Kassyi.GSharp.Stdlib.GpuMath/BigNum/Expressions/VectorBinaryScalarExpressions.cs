namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct VectorScalarOperands
{
    public VectorScalarOperands(GpuBigIntArrayView vector, GpuBigInt scalar, GpuBigIntArrayView destination)
    {
        Vector = vector;
        Scalar = scalar;
        Destination = destination;
    }

    public GpuBigIntArrayView Vector { get; }

    public GpuBigInt Scalar { get; }

    public GpuBigIntArrayView Destination { get; }
}

internal abstract class VectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    long bitsPerElement) : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    public int Length => vector.Length;
    public long BitsPerElement { get; } = bitsPerElement;
    public int WordsPerElement { get; } = BigIntWidths.Words(bitsPerElement);

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public int Capacity => vector.Capacity;

    public bool TryGetLength(out int length) => vector.TryGetLength(out length);

    // Evaluates the window of the capacity; the elements are the first Count of them
    public VectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        var evalCount = elementCount ?? (Capacity - elementOffset);
        // The target is checked before the children run, so that a rejected target evaluates nothing
        var targetValues = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetValues(targetResult, evalCount, WordsPerElement)
            : default;

        var vecResult = DeviceCounted.Evaluate(vector, workspace, elementOffset, evalCount);
        var scalarResult = scalar.Evaluate(workspace);
        try
        {
            // A target that shares words with an input is written from the cache after the launch (#864)
            var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, targetValues, vecResult.Storage,
                scalar: scalarResult.Value.Handle);
            var operands = new VectorScalarOperands(vecResult.Storage, scalarResult, output.Destination);
            // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
            if (evalCount == 0)
            {
                return new VectorEvaluation(output.Close(workspace), vecResult.Count, _cache.Lease);
            }

            if (DividesByZero(operands))
            {
                // C# divides only the elements, so a zero divisor throws only when there is one; after a Where that
                // selects none nothing is divided and nothing is written. Only this path reads a device count.
                if (vecResult.Count.Read(workspace) > 0)
                {
                    throw new DivideByZeroException();
                }

                return new VectorEvaluation(output.Close(workspace, DeviceElementCount.Static(0)), vecResult.Count,
                    _cache.Lease);
            }

            DispatchKernel(workspace, operands, (uint)evalCount);
            return new VectorEvaluation(output.Close(workspace), vecResult.Count, _cache.Lease);
        }
        finally
        {
            // An owned scalar result (a compound scalar expression) goes back to the pool; a leaf's is borrowed
            scalarResult.Dispose();
        }
    }

    protected GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation CreateScalarOp(VectorScalarOperands operands) =>
        new()
        {
            Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Vector.Values.ToProxy(),
                Signs = operands.Vector.Signs.ToProxy(),
                NumWords = vector.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Scalar.Value.ToProxy(),
                Signs = operands.Vector.Signs.ToProxy(),
                NumWords = operands.Scalar.Value.NumWords
            },
            Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
            {
                Limbs = operands.Destination.Values.ToProxy(),
                Signs = operands.Destination.Signs.ToProxy(),
                NumWords = WordsPerElement
            },
            ScalarSign = operands.Scalar.IsNegative ? 1u : 0u,
            IsScalar = true
        };

    /// <summary>
    /// Gets whether the operation is a division whose scalar divisor is zero (one read of the scalar).
    /// </summary>
    protected virtual bool DividesByZero(VectorScalarOperands operands) => false;

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX);

    public void Dispose()
    {
        // A cache release that throws (a failed capture refuses the clear) does not skip the children
        try
        {
            _cache.Dispose();
        }
        finally
        {
            vector.Dispose();
        }
    }
}

internal sealed class VectorMulScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.Multiply(vector.BitsPerElement, IBitBoundedExpression.Of(scalar)))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorMul
        {
            Op = CreateScalarOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorAddScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.Add(vector.BitsPerElement, IBitBoundedExpression.Of(scalar)))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorAddSub
        {
            Op = CreateScalarOp(operands),
            OpSign = 0u
        };
        GpuBigIntVectorKernels.LaunchVectorAddScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorSubScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.Add(vector.BitsPerElement, IBitBoundedExpression.Of(scalar)))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorAddSub
        {
            Op = CreateScalarOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorDivScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.OfWords(Math.Max(vector.WordsPerElement, scalar.NumWords)))
{
    protected override bool DividesByZero(VectorScalarOperands operands) => operands.Scalar.IsZero();

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateScalarOp(operands),
            IsModulo = false
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.Op, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
        GpuBigIntVectorKernels.LaunchVectorDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorModScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.OfWords(Math.Max(vector.WordsPerElement, scalar.NumWords)))
{
    protected override bool DividesByZero(VectorScalarOperands operands) => operands.Scalar.IsZero();

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateScalarOp(operands),
            IsModulo = true
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.Op, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
        GpuBigIntVectorKernels.LaunchVectorModScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
