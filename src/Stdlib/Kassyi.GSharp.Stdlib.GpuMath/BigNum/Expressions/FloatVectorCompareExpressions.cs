namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(vector);

    public bool MayThrow => _inputsMayThrow;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, first.Float));

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, FloatVectorEvaluation evaluated)
    {
        var flags = _flags.Get(workspace, Capacity);
        if (Capacity == 0)
        {
            return new BoolVectorEvaluation(flags, evaluated.Count);
        }

        var vecResult = evaluated.Storage;
        var scalarSig = scalar.Significand;
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            Left = FloatVectorCompareLaunch.Operand(vecResult),
            Right = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = scalarSig.ToProxy(),
                Exponents = vecResult.Exponents.ToProxy(),
                Signs = vecResult.Signs.ToProxy(),
                NumWords = scalarSig.NumWords
            },
            Result = flags.ToProxy(),
            ScalarExp = scalar.Exponent,
            ScalarSign = scalar.IsNegative ? 1u : 0u,
            IsScalar = true,
            OpType = (int)opType
        };

        FloatVectorCompareLaunch.Launch(workspace, ctx, Capacity, vecResult.WordsPerElement, scalarSig.NumWords);
        return new BoolVectorEvaluation(flags, evaluated.Count);
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

internal sealed class FloatVectorCompareVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    // The capacity of the right input, for the length check before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(left) || VectorEffects.MayThrow(right);

    // Inputs of different lengths are rejected
    public bool MayThrow => _inputsMayThrow || Capacity != _rightCapacity;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    // Evaluates the operands so that a shared sub-expression read at two offsets keeps both results
    private readonly FloatVectorOperandPair _operands = new();

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
        var leftEvaluated = first.Float;
        var rightEvaluated = second.Float;
        _operands.KeepBoth(left, workspace, 0, null, ref leftEvaluated, ref rightEvaluated);
        return VectorResult.Of(Apply(workspace, leftEvaluated, rightEvaluated));
    }

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, FloatVectorEvaluation leftEvaluated,
        FloatVectorEvaluation rightEvaluated)
    {
        var count = DeviceCounted.Combine(leftEvaluated.Count, rightEvaluated.Count);
        var flags = _flags.Get(workspace, Capacity);
        if (Capacity == 0)
        {
            return new BoolVectorEvaluation(flags, count);
        }

        var leftResult = leftEvaluated.Storage;
        var rightResult = rightEvaluated.Storage;
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            Left = FloatVectorCompareLaunch.Operand(leftResult),
            Right = FloatVectorCompareLaunch.Operand(rightResult),
            Result = flags.ToProxy(),
            ScalarExp = 0,
            ScalarSign = 0u,
            IsScalar = false,
            OpType = (int)opType
        };

        FloatVectorCompareLaunch.Launch(workspace, ctx, Capacity, leftResult.WordsPerElement,
            rightResult.WordsPerElement);
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
