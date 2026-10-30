namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct FloatVectorScalarOperands
{
    public FloatVectorScalarOperands(GpuBigFloatArrayView vector, GpuFloatOperand scalar, GpuBigFloatArrayView destination)
    {
        Vector = vector;
        Scalar = scalar;
        Destination = destination;
    }

    public GpuBigFloatArrayView Vector { get; }

    public GpuFloatOperand Scalar { get; }

    public GpuBigFloatArrayView Destination { get; }
}

internal abstract class FloatVectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    int wordsPerElement) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    protected const int DefaultDivShiftBits = 32;

    public int Length => vector.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept (design 4.6)
    private readonly VectorResultCache _cache = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public int Capacity => vector.Capacity;

    public bool TryGetLength(out int length) => vector.TryGetLength(out length);

    // Evaluates the window of the capacity; the elements are the first Count of them (design D3 4.3)
    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        var evalCount = elementCount ?? (Capacity - elementOffset);
        // The target is checked before the child runs, so that a rejected target evaluates nothing
        var target = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetFloat(targetResult, evalCount, WordsPerElement)
            : default;
        var vecResult = DeviceCounted.Evaluate(vector, workspace, elementOffset, evalCount);

        // A target that shares words with an input is written from the cache after the launch (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, target, vecResult.Storage,
            scalar: scalar.Significand);
        var operands = new FloatVectorScalarOperands(vecResult.Storage, scalar, output.Destination);
        var elements = RunsOnHost ? vecResult.Elements(workspace) : evalCount;
        // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
        if (elements > 0)
        {
            DispatchKernel(workspace, operands, (uint)elements);
        }

        // A host loop wrote only the elements; a kernel wrote the whole window
        var written = RunsOnHost ? vecResult.Count : DeviceElementCount.Static(evalCount);
        return new FloatVectorEvaluation(output.Close(workspace, written), vecResult.Count);
    }

    /// <summary>
    /// Whether the operation runs on the host element by element (Pow): it then runs over the elements only, not the
    /// rest of the capacity, which costs one synchronization when the count is on the device.
    /// </summary>
    protected virtual bool RunsOnHost => false;

    protected static GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation CreateScalarFloatOp(FloatVectorScalarOperands operands)
    {
        var scalarSig = operands.Scalar.Significand;
        return new GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation
        {
            Core = new GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation
            {
                Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = operands.Vector.Significands.DevicePointer,
                    Signs = operands.Vector.Signs.DevicePointer,
                    NumWords = operands.Vector.WordsPerElement
                },
                Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = scalarSig.DevicePointer,
                    Signs = operands.Vector.Signs.DevicePointer,
                    NumWords = scalarSig.NumWords
                },
                Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
                {
                    Limbs = operands.Destination.Significands.DevicePointer,
                    Signs = operands.Destination.Signs.DevicePointer,
                    NumWords = operands.Destination.WordsPerElement
                },
                ScalarSign = operands.Scalar.IsNegative ? 1u : 0u,
                IsScalar = true
            },
            Exponents = new GpuBigIntVectorKernels.AotContexts.FloatVectorExponents
            {
                ExpA = operands.Vector.Exponents.DevicePointer,
                ExpB = operands.Vector.Exponents.DevicePointer,
                ExpResult = operands.Destination.Exponents.DevicePointer,
                ScalarExp = operands.Scalar.Exponent
            }
        };
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
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

internal sealed class FloatVectorAddScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateScalarFloatOp(operands),
            OpSign = 0u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatAddScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorSubScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateScalarFloatOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorMulScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        vector.WordsPerElement + scalar.Significand.NumWords)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatMul
        {
            FloatOp = CreateScalarFloatOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorFloatMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorDivScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatDiv
        {
            FloatOp = CreateScalarFloatOp(operands),
            PreShiftBits = DefaultDivShiftBits
        };
        GpuBigIntVectorKernels.LaunchVectorFloatDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorPowScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand exponentScalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        exponentScalar,
        Math.Max(vector.WordsPerElement, exponentScalar.Significand.NumWords))
{
    protected override bool RunsOnHost => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var expOperand = operands.Scalar;
        FloatVectorElementwiseHelper.ExecuteUnary(
            workspace,
            operands.Vector,
            operands.Destination,
            (int)gridDimX,
            (ref GpuBigIntExecutionContext ctx, IGpuBigFloatArithmetic arith, in GpuFloatOperand src, GpuBigIntMemoryHandle dst) =>
                arith.Pow(ref ctx, in src, in expOperand, dst));
    }
}
