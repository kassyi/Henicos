namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct FloatVectorZipOperands
{
    public FloatVectorZipOperands(GpuBigFloatArrayView left, GpuBigFloatArrayView right, GpuBigFloatArrayView destination)
    {
        Left = left;
        Right = right;
        Destination = destination;
    }

    public GpuBigFloatArrayView Left { get; }

    public GpuBigFloatArrayView Right { get; }

    public GpuBigFloatArrayView Destination { get; }
}

internal abstract class FloatVectorBinaryVectorExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    int wordsPerElement) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    protected const int DefaultDivShiftBits = 32;

    public int Length => left.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept (design 4.6)
    private readonly VectorResultCache _cache = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public int Capacity => left.Capacity;

    public bool TryGetLength(out int length) => left.TryGetLength(out length);

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    // Evaluates the window of the capacity; the elements are the first Count of them (design D3 4.3)
    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        if (left.Capacity != right.Capacity)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        var evalCount = elementCount ?? (Capacity - elementOffset);
        // The target is checked before the children run, so that a rejected target evaluates nothing
        var target = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetFloat(targetResult, evalCount, WordsPerElement)
            : default;
        var leftResult = DeviceCounted.Evaluate(left, workspace, elementOffset, evalCount);
        var rightResult = DeviceCounted.Evaluate(right, workspace, elementOffset, evalCount);

        if (leftResult.Storage.Length != rightResult.Storage.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);
        // A target that shares words with an input is written from the cache after the launch (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, target, leftResult.Storage,
            rightResult.Storage);
        var operands = new FloatVectorZipOperands(leftResult.Storage, rightResult.Storage, output.Destination);
        var elements = RunsOnHost && !count.IsStatic ? count.Read(workspace) : evalCount;
        // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
        if (elements > 0)
        {
            DispatchKernel(workspace, operands, (uint)elements);
        }

        return new FloatVectorEvaluation(output.Close(workspace), count);
    }

    /// <summary>
    /// Whether the operation runs on the host element by element (Pow): it then runs over the elements only, not the
    /// rest of the capacity, which costs one synchronization when the count is on the device.
    /// </summary>
    protected virtual bool RunsOnHost => false;


    protected static GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation CreateZipFloatOp(FloatVectorZipOperands operands) =>
        new()
        {
            Core = new GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation
            {
                Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = operands.Left.Significands.DevicePointer,
                    Signs = operands.Left.Signs.DevicePointer,
                    NumWords = operands.Left.WordsPerElement
                },
                Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = operands.Right.Significands.DevicePointer,
                    Signs = operands.Right.Signs.DevicePointer,
                    NumWords = operands.Right.WordsPerElement
                },
                Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
                {
                    Limbs = operands.Destination.Significands.DevicePointer,
                    Signs = operands.Destination.Signs.DevicePointer,
                    NumWords = operands.Destination.WordsPerElement
                },
                ScalarSign = 0u,
                IsScalar = false
            },
            Exponents = new GpuBigIntVectorKernels.AotContexts.FloatVectorExponents
            {
                ExpA = operands.Left.Exponents.DevicePointer,
                ExpB = operands.Right.Exponents.DevicePointer,
                ExpResult = operands.Destination.Exponents.DevicePointer,
                ScalarExp = 0
            }
        };

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
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

internal sealed class FloatVectorAddVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateZipFloatOp(operands),
            OpSign = 0u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatZipAddKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorSubVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateZipFloatOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatZipSubKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorMulVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        left.WordsPerElement + right.WordsPerElement)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatMul
        {
            FloatOp = CreateZipFloatOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorFloatZipMulKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorDivVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatDiv
        {
            FloatOp = CreateZipFloatOp(operands),
            PreShiftBits = DefaultDivShiftBits
        };
        GpuBigIntVectorKernels.LaunchVectorFloatZipDivKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorPowVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement))
{
    protected override bool RunsOnHost => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX) =>
        FloatVectorElementwiseHelper.ExecuteBinary(
            workspace,
            operands.Left,
            operands.Right,
            operands.Destination,
            (int)gridDimX,
            static (ref ctx, arith, in l, in r, dst) =>
                arith.Pow(ref ctx, in l, in r, dst));
}
