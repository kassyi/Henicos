namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorTakeExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _source = VectorShape.Of(source);

    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity => Math.Min(_source.Capacity, count);

    public int WordsPerElement { get; } = source.WordsPerElement;
    public long BitsPerElement { get; } = source.BitsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = _source.TryGetLength(out var sourceLength);
        length = known ? Math.Min(sourceLength, count) : 0;
        return known;
    }

    public int OperandCount => 1;

    public object GetOperand(int index) => source;

    // An empty window evaluates nothing
    public bool EvaluatesOperands(in VectorRequest request) => EffectiveCount(request) > 0;

    // The source evaluates the window into the caller's target
    public VectorRequest OperandRequest(in VectorRequest request) =>
        new(request.Offset, EffectiveCount(request), request.Target);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        EvaluatesOperands(request)
            ? first
            : VectorSlice.EmptyResult(workspace, WordsPerElement, request);

    // Ensure we do not evaluate beyond the Take boundary; a device count below it is cut by the source's TakeFirst
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

        return Apply(workspace, request, VectorWalk.EvaluateDirect(source, workspace, OperandRequest(request), level),
            default);
    }

    public void DisposeOwn()
    {
        // Holds nothing of its own
    }

    public void Dispose() => VectorWalk.Dispose(this);
}

internal static class VectorSlice
{
    // The result of an empty window; out of line, so that its temporaries stay out of the frame of every level
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static VectorResult EmptyResult(GpuWorkspace workspace, int wordsPerElement,
        scoped in VectorRequest request) =>
        VectorResult.Of(new VectorEvaluation(Empty(workspace, wordsPerElement, request.Target.Words),
            DeviceElementCount.Static(0)));

    // An evaluation of no elements writes nothing, but a target of another width is still rejected, as by every node
    public static GpuBigIntArrayView Empty(GpuWorkspace workspace, int wordsPerElement, GpuBigUIntArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            VectorResultCache.TargetValues(targetResult, 0, wordsPerElement);
        }

        return VectorResultCache.EmptySigned(workspace, wordsPerElement);
    }
}

internal class VectorSkipExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _source = VectorShape.Of(source);

    // Skipping past the capacity skips everything; a device count below the skip gives 0 through SkipFirst
    private readonly int _skipCount = Math.Min(count, source.Capacity);

    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity => _source.Capacity - _skipCount;

    public int WordsPerElement { get; } = source.WordsPerElement;
    public long BitsPerElement { get; } = source.BitsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = _source.TryGetLength(out var sourceLength);
        length = known ? sourceLength - _skipCount : 0;
        return known;
    }

    public int OperandCount => 1;

    public object GetOperand(int index) => source;

    // An empty window evaluates nothing
    public bool EvaluatesOperands(in VectorRequest request) => request.EvalCount(Capacity) > 0;

    // Pass the requested element slice but shifted by the Skip count, into the caller's target
    public VectorRequest OperandRequest(in VectorRequest request) =>
        new(request.Offset + _skipCount, request.EvalCount(Capacity), request.Target);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        EvaluatesOperands(request)
            ? first
            : VectorSlice.EmptyResult(workspace, WordsPerElement, request);

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        if (!EvaluatesOperands(request))
        {
            return Apply(workspace, request, default, default);
        }

        return Apply(workspace, request, VectorWalk.EvaluateDirect(source, workspace, OperandRequest(request), level),
            default);
    }

    public void DisposeOwn()
    {
        // Holds nothing of its own
    }

    public void Dispose() => VectorWalk.Dispose(this);
}
