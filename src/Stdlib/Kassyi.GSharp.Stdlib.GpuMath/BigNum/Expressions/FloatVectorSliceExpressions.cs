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

internal static class FloatVectorSlice
{
    // The result of an empty window; out of line, so that its temporaries stay out of the frame of every level
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static VectorResult EmptyResult(GpuWorkspace workspace, int wordsPerElement,
        scoped in VectorRequest request) =>
        VectorResult.Of(new FloatVectorEvaluation(Empty(workspace, wordsPerElement, request.Target.FloatArray),
            DeviceElementCount.Static(0)));

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
