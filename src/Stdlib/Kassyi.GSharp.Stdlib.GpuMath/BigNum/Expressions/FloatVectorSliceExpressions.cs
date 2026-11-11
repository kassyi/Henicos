namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    private readonly VectorShape _inner = VectorShape.Of(inner);

    // Skipping past the capacity skips everything; a device count below the skip gives 0 through SkipFirst
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Capacity);

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

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second) =>
        EvaluatesOperands(request)
            ? first
            : VectorValue.Of(new FloatVectorEvaluation(
                FloatVectorSlice.Empty(workspace, WordsPerElement, request.Target.FloatArray),
                DeviceElementCount.Static(0)));

    public void DisposeOwn()
    {
        // Holds nothing of its own
    }

    public void Dispose() => VectorWalk.DisposeInputs(this);
}

internal static class FloatVectorSlice
{
    // An evaluation of no elements writes nothing, but a target of another width is still rejected, as by every node
    public static GpuBigFloatArrayView Empty(GpuWorkspace workspace, int wordsPerElement, GpuBigFloatArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            VectorResultCache.TargetFloat(targetResult, 0, wordsPerElement);
        }

        return VectorResultCache.EmptyFloat(workspace, wordsPerElement);
    }
}

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    private readonly VectorShape _inner = VectorShape.Of(inner);

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

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second) =>
        EvaluatesOperands(request)
            ? first
            : VectorValue.Of(new FloatVectorEvaluation(
                FloatVectorSlice.Empty(workspace, WordsPerElement, request.Target.FloatArray),
                DeviceElementCount.Static(0)));

    private int EffectiveCount(in VectorRequest request) =>
        Math.Min(request.EvalCount(Capacity), Capacity - request.Offset);

    public void DisposeOwn()
    {
        // Holds nothing of its own
    }

    public void Dispose() => VectorWalk.DisposeInputs(this);
}
