namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct VectorZipOperands
{
    public VectorZipOperands(GpuBigIntArrayView left, GpuBigIntArrayView right, GpuBigIntArrayView destination)
    {
        Left = left;
        Right = right;
        Destination = destination;
    }

    public GpuBigIntArrayView Left { get; }

    public GpuBigIntArrayView Right { get; }

    public GpuBigIntArrayView Destination { get; }
}

internal abstract class VectorBinaryVectorExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
    int wordsPerElement,
    bool checkConstructorLength = false) : IGpuBigIntVectorExpression
{
    public int Length => left.Length;
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
        if (checkConstructorLength && left.Length != right.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        var evalCount = elementCount ?? (Length - elementOffset);
        // The target is checked before the children run, so that a rejected target evaluates nothing
        var targetValues = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetValues(targetResult, evalCount, WordsPerElement)
            : default;

        var leftResult = left.Evaluate(workspace, elementOffset, evalCount);
        var rightResult = right.Evaluate(workspace, elementOffset, evalCount);

        if (leftResult.Length != rightResult.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        // A target that shares words with an input is written from the cache after the launch (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, targetValues, leftResult,
            rightResult);
        var operands = new VectorZipOperands(leftResult, rightResult, output.Destination);
        // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
        if (evalCount > 0)
        {
            DispatchKernel(workspace, operands, (uint)evalCount);
        }
        return output.Close(workspace);
    }

    protected GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation CreateZipOp(VectorZipOperands operands) =>
        new()
        {
            Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Left.Values.DevicePointer,
                Signs = operands.Left.Signs.DevicePointer,
                NumWords = operands.Left.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Right.Values.DevicePointer,
                Signs = operands.Right.Signs.DevicePointer,
                NumWords = operands.Right.WordsPerElement
            },
            Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
            {
                Limbs = operands.Destination.Values.DevicePointer,
                Signs = operands.Destination.Signs.DevicePointer,
                NumWords = WordsPerElement
            },
            ScalarSign = 0u,
            IsScalar = false
        };

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
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
            try
            {
                left.Dispose();
            }
            finally
            {
                right.Dispose();
            }
        }
    }
}

internal sealed class VectorAddVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorAddSub
        {
            Op = CreateZipOp(operands),
            OpSign = 0u
        };
        GpuBigIntVectorKernels.LaunchVectorZipAddKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorSubVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, Math.Max(left.WordsPerElement, right.WordsPerElement))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorAddSub
        {
            Op = CreateZipOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorZipSubKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorMulVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, left.WordsPerElement + right.WordsPerElement)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorMul
        {
            Op = CreateZipOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorZipMulKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorDivVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement),
        checkConstructorLength: true)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateZipOp(operands),
            IsModulo = false
        };
        GpuBigIntVectorKernels.LaunchVectorZipDivKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorModVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement),
        checkConstructorLength: true)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateZipOp(operands),
            IsModulo = true
        };
        GpuBigIntVectorKernels.LaunchVectorZipModKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
