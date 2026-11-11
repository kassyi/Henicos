namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    private readonly VectorShape _inner = VectorShape.Of(inner);

    // Skipping past the capacity skips everything; a device count below the skip gives 0 through SkipFirst
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Capacity);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(inner);

    // A slice adds no check; a projection outside it is not evaluated, as System.Linq
    public bool MayThrow => _inputsMayThrow;

    public int Length => DeviceCounted.KnownLength(this);
    public int Capacity => _inner.Capacity - _effectiveSkip;
    public int WordsPerElement { get; } = inner.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = _inner.TryGetLength(out var innerLength);
        length = known ? innerLength - _effectiveSkip : 0;
        return known;
    }

    public int OperandCount => 1;

    public object GetOperand(int index) => inner;

    // An empty window evaluates nothing
    public bool EvaluatesOperands(in VectorRequest request) => request.EvalCount(Capacity) > 0;

    // The window shifted by the Skip count, into the caller's target
    public VectorRequest OperandRequest(in VectorRequest request) =>
        new(_effectiveSkip + request.Offset, request.EvalCount(Capacity), request.Target);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        EvaluatesOperands(request)
            ? first
            : FloatVectorSlice.EmptyResult(workspace, WordsPerElement, request);

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        if (!EvaluatesOperands(request))
        {
            return Apply(workspace, request, default, default);
        }

        return Apply(workspace, request, VectorWalk.EvaluateDirect(inner, workspace, OperandRequest(request), level),
            default);
    }

    public void DisposeOwn()
    {
        // Holds nothing of its own
    }

    public void Dispose() => VectorWalk.Dispose(this);
}
