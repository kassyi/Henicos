namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorCompareVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
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
