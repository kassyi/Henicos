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
    int wordsPerElement) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept (design 4.6)
    private readonly VectorResultCache _cache = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public GpuBigIntArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigUIntArrayView targetResult = default)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        // The target is checked before the children run, so that a rejected target evaluates nothing
        var targetValues = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetValues(targetResult, evalCount, WordsPerElement)
            : default;

        var vecResult = vector.Evaluate(workspace, elementOffset, evalCount);
        var scalarResult = scalar.Evaluate(workspace);
        try
        {
            // A target that shares words with an input is written from the cache after the launch (#864)
            var staged = DeviceRanges.Overlaps(targetValues, vecResult) ||
                DeviceRanges.Overlaps(targetValues.Handle, scalarResult.Value.Handle);
            var destination = _cache.Signed(workspace, evalCount, WordsPerElement, staged ? default : targetValues);
            var operands = new VectorScalarOperands(vecResult, scalarResult, destination);
            // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
            if (evalCount > 0)
            {
                DispatchKernel(workspace, operands, (uint)evalCount);
            }

            return staged ? VectorResultCache.Deliver(workspace, destination, targetValues) : destination;
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
                Limbs = operands.Vector.Values.DevicePointer,
                Signs = operands.Vector.Signs.DevicePointer,
                NumWords = vector.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Scalar.Value.DevicePointer,
                Signs = operands.Vector.Signs.DevicePointer,
                NumWords = operands.Scalar.Value.NumWords
            },
            Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
            {
                Limbs = operands.Destination.Values.DevicePointer,
                Signs = operands.Destination.Signs.DevicePointer,
                NumWords = WordsPerElement
            },
            ScalarSign = operands.Scalar.IsNegative ? 1u : 0u,
            IsScalar = true
        };

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
    : VectorBinaryScalarExpressionBase(vector, scalar, vector.WordsPerElement + scalar.NumWords)
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
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords) + 1)
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
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords))
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
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        if (operands.Scalar.IsZero())
        {
            throw new DivideByZeroException();
        }

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateScalarOp(operands),
            IsModulo = false
        };
        GpuBigIntVectorKernels.LaunchVectorDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorModScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        if (operands.Scalar.IsZero())
        {
            throw new DivideByZeroException();
        }

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateScalarOp(operands),
            IsModulo = true
        };
        GpuBigIntVectorKernels.LaunchVectorModScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
