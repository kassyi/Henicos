namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorCompareScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, first.Int));

    // The flags of the capacity; the elements are the first Count of them, as for the input
    private BoolVectorEvaluation Apply(GpuWorkspace workspace, VectorEvaluation vecResult)
    {
        var scalarResult = scalar.Evaluate(workspace);
        try
        {
            var flags = _flags.Get(workspace, Capacity);

            // The scalar is read with its own word count and sign: it may be narrower or wider than the elements, and
            // it may be a view into a larger buffer whose following words are not 0.
            var ctx = new GpuBigIntCmpKernels.AotContexts.VectorCompare
            {
                Left = VectorCompareLaunch.Operand(vecResult.Storage),
                Right = VectorCompareLaunch.Scalar(scalarResult),
                Result = flags.ToProxy(),
                OpType = (int)opType
            };

            VectorCompareLaunch.Launch(workspace, ctx, Capacity, vecResult.Storage.WordsPerElement,
                scalarResult.Value.NumWords);
            return new BoolVectorEvaluation(flags, vecResult.Count);
        }
        finally
        {
            // An owned scalar result (a compound scalar expression) goes back to the pool; a leaf's is borrowed
            scalarResult.Dispose();
        }
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        return Apply(workspace, request, VectorWalk.EvaluateDirect(vector, workspace, VectorRequest.Whole, level),
            default);
    }

    public void DisposeOwn() => _flags.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}

internal class VectorCompareVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    // The capacity of the right input, for the length check before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    // Evaluates the operands so that a shared sub-expression read at two offsets keeps both results
    private readonly VectorOperandPair _operands = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 2;

    public object GetOperand(int index) => index == 0 ? left : right;

    // The lengths are checked before the inputs are evaluated, so that a rejected request evaluates nothing
    public void Enter(in VectorRequest request) =>
        VectorShape.ThrowIfLengthsDiffer(Capacity, _rightCapacity, "Lengths must match");

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second)
    {
        var leftResult = first.Int;
        var rightResult = second.Int;
        _operands.KeepBoth(left, workspace, 0, null, ref leftResult, ref rightResult);
        return VectorResult.Of(Apply(workspace, leftResult, rightResult));
    }

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, VectorEvaluation leftResult,
        VectorEvaluation rightResult)
    {
        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);
        var flags = _flags.Get(workspace, Capacity);

        // Each side is read with its own stride and word count (the two may differ).
        var ctx = new GpuBigIntCmpKernels.AotContexts.VectorCompare
        {
            Left = VectorCompareLaunch.Operand(leftResult.Storage),
            Right = VectorCompareLaunch.Operand(rightResult.Storage),
            Result = flags.ToProxy(),
            OpType = (int)opType
        };

        VectorCompareLaunch.Launch(workspace, ctx, Capacity, leftResult.Storage.WordsPerElement,
            rightResult.Storage.WordsPerElement);
        return new BoolVectorEvaluation(flags, count);
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        Enter(request);

        var first = VectorWalk.EvaluateDirect(left, workspace, VectorRequest.Whole, level);
        var second = VectorWalk.EvaluateDirect(right, workspace, VectorRequest.Whole, level);
        return Apply(workspace, request, first, second);
    }

    public void DisposeOwn()
    {
        // A flag release that throws (a failed capture) does not skip the pair
        try
        {
            _flags.Dispose();
        }
        finally
        {
            _operands.Dispose();
        }
    }

    public void Dispose() => VectorWalk.Dispose(this);
}

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
