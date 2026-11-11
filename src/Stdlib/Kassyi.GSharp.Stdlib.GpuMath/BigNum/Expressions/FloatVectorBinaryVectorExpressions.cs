namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct FloatVectorZipOperands
{
    public FloatVectorZipOperands(
        GpuBigFloatArrayView left,
        GpuBigFloatArrayView right,
        GpuBigFloatArrayView destination,
        GpuBigUIntArrayView zeroDivisor)
    {
        Left = left;
        Right = right;
        Destination = destination;
        ZeroDivisor = zeroDivisor;
    }

    public GpuBigFloatArrayView Left { get; }

    public GpuBigFloatArrayView Right { get; }

    public GpuBigFloatArrayView Destination { get; }

    /// <summary>Gets the word of <see cref="ZeroDivisorCheck"/> for a division, or the default view.</summary>
    public GpuBigUIntArrayView ZeroDivisor { get; }
}

internal abstract class FloatVectorBinaryVectorExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    int wordsPerElement) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    // The capacity of the right input, for the length check of a Zip before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(left) || VectorEffects.MayThrow(right);

    public int Length => _shape.Length;

    // Inputs of different lengths are rejected
    public bool MayThrow => _inputsMayThrow || ElementMayThrow || Capacity != _rightCapacity;

    /// <summary>
    /// Gets whether the operation itself can throw for an element (a zero divisor, a negative square root, a value the
    /// host arithmetic cannot compute).
    /// </summary>
    protected virtual bool ElementMayThrow => ChecksZeroDivisor || RunsOnHost;
    public int WordsPerElement { get; } = wordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

    // Evaluates the operands so that a shared sub-expression read at two offsets keeps both results
    private readonly FloatVectorOperandPair _operands = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    /// <summary>Gets whether the kernel reports a zero divisor element (<see cref="ZeroDivisorCheck"/>).</summary>
    protected virtual bool ChecksZeroDivisor => false;

    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 2;

    public object GetOperand(int index) => index == 0 ? left : right;

    // The lengths and the target are checked before the inputs are evaluated, so that a rejected request evaluates
    // nothing
    public void Enter(in VectorRequest request)
    {
        VectorShape.ThrowIfLengthsDiffer(Capacity, _rightCapacity, VectorShape.ZipLengthsMessage);
        request.FloatTarget(Capacity, WordsPerElement);
    }

    // The window of the capacity; the elements are the first Count of them
    public VectorRequest OperandRequest(in VectorRequest request) => request.WindowOf(Capacity);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second)
    {
        var evalCount = request.EvalCount(Capacity);
        var leftResult = first.Float;
        var rightResult = second.Float;
        _operands.KeepBoth(left, workspace, request.Offset, evalCount, ref leftResult, ref rightResult);
        return VectorResult.Of(Apply(workspace, evalCount, request.FloatTarget(Capacity, WordsPerElement), leftResult,
            rightResult));
    }

    private FloatVectorEvaluation Apply(GpuWorkspace workspace, int evalCount, GpuBigFloatArrayView target,
        FloatVectorEvaluation leftResult, FloatVectorEvaluation rightResult)
    {
        VectorShape.ThrowIfEvaluatedLengthsDiffer(leftResult.Storage.Length, rightResult.Storage.Length);

        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);
        // A target that shares words with an input is written from the cache after the launch (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, target, leftResult.Storage,
            rightResult.Storage);
        var elements = RunsOnHost && !count.IsStatic ? count.Read(workspace) : evalCount;
        // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
        if (elements > 0)
        {
            var zeroDivisor = ChecksZeroDivisor ? ZeroDivisorCheck.Open(workspace, _cache) : default;
            var operands = new FloatVectorZipOperands(leftResult.Storage, rightResult.Storage, output.Destination,
                zeroDivisor);
            DispatchKernel(workspace, operands, (uint)elements);
            if (ChecksZeroDivisor)
            {
                ZeroDivisorCheck.ThrowIfFound(workspace, zeroDivisor, count);
            }
        }

        // A host loop wrote only the elements; a kernel wrote the whole window
        var written = RunsOnHost ? count : DeviceElementCount.Static(evalCount);
        return new FloatVectorEvaluation(output.Close(workspace, written), count, _cache.Lease);
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
                    Limbs = operands.Left.Significands.ToProxy(),
                    Signs = operands.Left.Signs.ToProxy(),
                    NumWords = operands.Left.WordsPerElement
                },
                Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = operands.Right.Significands.ToProxy(),
                    Signs = operands.Right.Signs.ToProxy(),
                    NumWords = operands.Right.WordsPerElement
                },
                Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
                {
                    Limbs = operands.Destination.Significands.ToProxy(),
                    Signs = operands.Destination.Signs.ToProxy(),
                    NumWords = operands.Destination.WordsPerElement
                },
                ScalarSign = 0u,
                IsScalar = false
            },
            Exponents = new GpuBigIntVectorKernels.AotContexts.FloatVectorExponents
            {
                ExpA = operands.Left.Exponents.ToProxy(),
                ExpB = operands.Right.Exponents.ToProxy(),
                ExpResult = operands.Destination.Exponents.ToProxy(),
                ScalarExp = 0
            }
        };

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX);

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        Enter(request);

        var operandRequest = OperandRequest(request);
        var first = VectorWalk.EvaluateDirect(left, workspace, operandRequest, level);
        var second = VectorWalk.EvaluateDirect(right, workspace, operandRequest, level);
        return Apply(workspace, request, first, second);
    }

    public void DisposeOwn()
    {
        // A cache release that throws (a failed capture refuses the clear) does not skip the other one
        try
        {
            _cache.Dispose();
        }
        finally
        {
            _operands.Dispose();
        }
    }

    public void Dispose() => VectorWalk.Dispose(this);
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
    protected override bool ChecksZeroDivisor => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatDiv
        {
            FloatOp = CreateZipFloatOp(operands),
            ZeroDivisor = operands.ZeroDivisor.ToProxy()
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.FloatOp.Core, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
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
