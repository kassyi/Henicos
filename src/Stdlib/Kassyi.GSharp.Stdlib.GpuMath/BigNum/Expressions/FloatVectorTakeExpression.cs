namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    private readonly VectorShape _inner = VectorShape.Of(inner);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(inner);

    // A slice adds no check; a projection outside it is not evaluated, as System.Linq
    public bool MayThrow => _inputsMayThrow;

    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity => Math.Clamp(takeCount, 0, _inner.Capacity);

    public int WordsPerElement { get; } = inner.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = _inner.TryGetLength(out var innerLength);
        length = known ? Math.Clamp(takeCount, 0, innerLength) : 0;
        return known;
    }

    public int OperandCount => 1;

    public object GetOperand(int index) => inner;

    // An empty window evaluates nothing
    public bool EvaluatesOperands(in VectorRequest request) => EffectiveCount(request) > 0;

    // The window, cut at the Take boundary, into the caller's target
    public VectorRequest OperandRequest(in VectorRequest request) =>
        new(request.Offset, EffectiveCount(request), request.Target);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        EvaluatesOperands(request)
            ? first
            : FloatVectorSlice.EmptyResult(workspace, WordsPerElement, request);

    private int EffectiveCount(in VectorRequest request) =>
        Math.Min(request.EvalCount(Capacity), Capacity - request.Offset);

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
