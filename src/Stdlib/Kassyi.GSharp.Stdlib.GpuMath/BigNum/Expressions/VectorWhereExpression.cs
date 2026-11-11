namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Keeps the elements whose predicate is true. The number of selected elements is computed on the
/// device into a word this node owns, so neither the selection nor its consumers (Count, Sum, Skip, ...) synchronize
/// with the host; only the public <c>Evaluate</c> reads it, to return exactly the selected elements.
/// </summary>
internal class VectorWhereExpression(
    [TakesOwnership] IGpuBigIntVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigIntVectorExpression, IIntVectorOperator
{
    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity { get; } = source.Capacity;

    // A predicate of another length is rejected
    public bool MayThrow { get; } = VectorEffects.MayThrow(source) || VectorEffects.MayThrow(predicate) ||
        predicate.Capacity != source.Capacity;

    public int WordsPerElement { get; } = source.WordsPerElement;
    public long BitsPerElement { get; } = source.BitsPerElement;

    // The selected elements, the scan of the predicates, the count word and the block sums of the scan: owned here and
    // reused, so that an evaluation allocates nothing after the first
    private readonly WhereDeviceState _state = new();

    public bool TryGetLength(out int length)
    {
        length = 0;
        return false;
    }

    public int OperandCount => 2;

    // The predicate first: when the source and the predicate read one shared sub-expression at different offsets
    // (e.Skip(1).Where(e.Take(3).IsGreaterThan(x))), the later evaluation overwrites that node's cache. The flags
    // are in the predicate's own buffer, which a source evaluation leaves as it is (a bool vector is always read
    // over its whole length, so evaluating a shared bool node again writes the same flags); the source evaluated
    // second keeps its values.
    public object GetOperand(int index) => index == 0 ? predicate : source;

    // Released in the order of the node: the source, then the predicate
    public object GetInput(int index) => index == 0 ? source : predicate;

    public void Enter(in VectorRequest request) =>
        WhereDeviceState.ThrowIfCannotSelect(VectorResultCache.HasTarget(request.Target.Words), Capacity, predicate);

    // An empty vector evaluates nothing
    public bool EvaluatesOperands(in VectorRequest request) => Capacity != 0;

    // The source and the predicate are read whole; the selection is cut to the window
    public VectorRequest OperandRequest(in VectorRequest request) => VectorRequest.Whole;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second)
    {
        if (Capacity == 0)
        {
            return VectorResult.Of(new VectorEvaluation(VectorResultCache.EmptySigned(workspace, WordsPerElement),
                DeviceElementCount.Static(0)));
        }

        var evaluatedSource = second.Int;
        var selected = _state.Select<IntVectorResultKind, GpuBigIntArrayView>(workspace, evaluatedSource.Storage,
            evaluatedSource.Count, first.Bool, Capacity, WordsPerElement, request.Offset, request.Count,
            out var count);
        return VectorResult.Of(new VectorEvaluation(selected, count));
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        Enter(request);
        if (!EvaluatesOperands(request))
        {
            return Apply(workspace, request, default, default);
        }

        var operandRequest = OperandRequest(request);
        var first = VectorWalk.EvaluateDirect(predicate, workspace, operandRequest, level);
        var second = VectorWalk.EvaluateDirect(source, workspace, operandRequest, level);
        return Apply(workspace, request, first, second);
    }

    public void DisposeOwn() => _state.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}
