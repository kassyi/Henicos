namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct VectorZipOperands
{
    public VectorZipOperands(
        GpuBigIntArrayView left,
        GpuBigIntArrayView right,
        GpuBigIntArrayView destination,
        GpuBigUIntArrayView zeroDivisor)
    {
        Left = left;
        Right = right;
        Destination = destination;
        ZeroDivisor = zeroDivisor;
    }

    public GpuBigIntArrayView Left { get; }

    public GpuBigIntArrayView Right { get; }

    public GpuBigIntArrayView Destination { get; }

    /// <summary>Gets the word of <see cref="ZeroDivisorCheck"/> for a division, or the default view.</summary>
    public GpuBigUIntArrayView ZeroDivisor { get; }
}

internal abstract class VectorBinaryVectorExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
    long bitsPerElement,
    bool checkConstructorLength = false) : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    // The capacity of the right input, for the length check of a Zip before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;

    public int Length => _shape.Length;
    public long BitsPerElement { get; } = bitsPerElement;
    public int WordsPerElement { get; } = BigIntWidths.Words(bitsPerElement);

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

    // Evaluates the operands so that a shared sub-expression read at two offsets keeps both results
    private readonly VectorOperandPair _operands = new();

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
        if (checkConstructorLength && Capacity != _rightCapacity)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        request.IntTarget(Capacity, WordsPerElement);
    }

    // The window of the capacity; the elements are the first Count of them
    public VectorRequest OperandRequest(in VectorRequest request) =>
        VectorRequest.Window(request.Offset, request.EvalCount(Capacity));

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second)
    {
        var evalCount = request.EvalCount(Capacity);
        var leftResult = first.Int;
        var rightResult = second.Int;
        _operands.KeepBoth(left, workspace, request.Offset, evalCount, ref leftResult, ref rightResult);
        return VectorValue.Of(Apply(workspace, evalCount, request.IntTarget(Capacity, WordsPerElement), leftResult,
            rightResult));
    }

    private VectorEvaluation Apply(GpuWorkspace workspace, int evalCount, GpuBigUIntArrayView targetValues,
        VectorEvaluation leftResult, VectorEvaluation rightResult)
    {
        if (leftResult.Storage.Length != rightResult.Storage.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);
        // A target that shares words with an input is written from the cache after the launch (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, targetValues, leftResult.Storage,
            rightResult.Storage);
        // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
        if (evalCount > 0)
        {
            var zeroDivisor = ChecksZeroDivisor ? ZeroDivisorCheck.Open(workspace, _cache) : default;
            var operands = new VectorZipOperands(leftResult.Storage, rightResult.Storage, output.Destination,
                zeroDivisor);
            DispatchKernel(workspace, operands, (uint)evalCount);
            if (ChecksZeroDivisor)
            {
                ZeroDivisorCheck.ThrowIfFound(workspace, zeroDivisor, count);
            }
        }

        return new VectorEvaluation(output.Close(workspace), count, _cache.Lease);
    }

    protected GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation CreateZipOp(VectorZipOperands operands) =>
        new()
        {
            Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Left.Values.ToProxy(),
                Signs = operands.Left.Signs.ToProxy(),
                NumWords = operands.Left.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Right.Values.ToProxy(),
                Signs = operands.Right.Signs.ToProxy(),
                NumWords = operands.Right.WordsPerElement
            },
            Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
            {
                Limbs = operands.Destination.Values.ToProxy(),
                Signs = operands.Destination.Signs.ToProxy(),
                NumWords = WordsPerElement
            },
            ScalarSign = 0u,
            IsScalar = false
        };

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX);

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

    public void Dispose()
    {
        try
        {
            DisposeOwn();
        }
        finally
        {
            VectorWalk.DisposeInputs(this);
        }
    }
}

internal sealed class VectorAddVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, BigIntWidths.Add(left.BitsPerElement, right.BitsPerElement))
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
    : VectorBinaryVectorExpressionBase(left, right, BigIntWidths.Add(left.BitsPerElement, right.BitsPerElement))
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
    : VectorBinaryVectorExpressionBase(left, right,
        BigIntWidths.Multiply(left.BitsPerElement, right.BitsPerElement))
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
        BigIntWidths.OfWords(Math.Max(left.WordsPerElement, right.WordsPerElement)),
        checkConstructorLength: true)
{
    protected override bool ChecksZeroDivisor => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateZipOp(operands),
            IsModulo = false,
            ZeroDivisor = operands.ZeroDivisor.ToProxy()
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.Op, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
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
        BigIntWidths.OfWords(Math.Max(left.WordsPerElement, right.WordsPerElement)),
        checkConstructorLength: true)
{
    protected override bool ChecksZeroDivisor => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateZipOp(operands),
            IsModulo = true,
            ZeroDivisor = operands.ZeroDivisor.ToProxy()
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.Op, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
        GpuBigIntVectorKernels.LaunchVectorZipModKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
