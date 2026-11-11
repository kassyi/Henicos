namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The floating-point <c>Where</c>: the number of selected elements is computed on the device into a
/// word this node owns, as in <see cref="VectorWhereExpression"/>.
/// </summary>
internal sealed class FloatVectorWhereExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity { get; } = source.Capacity;

    // A predicate of another length is rejected
    public bool MayThrow { get; } = VectorEffects.MayThrow(source) || VectorEffects.MayThrow(predicate) ||
        predicate.Capacity != source.Capacity;

    public int WordsPerElement { get; } = source.WordsPerElement;

    // The selected elements, the scan of the predicates, the count word and the block sums of the scan: owned here and
    // reused, so that an evaluation allocates nothing after the first
    private readonly WhereDeviceState _state = new();

    public bool TryGetLength(out int length)
    {
        length = 0;
        return false;
    }

    public int OperandCount => 2;

    // The predicate first, as in VectorWhereExpression: a source that shares a sub-expression with it is
    // evaluated last and keeps its values.
    public object GetOperand(int index) => index == 0 ? predicate : source;

    // Released in the order of the node: the source, then the predicate
    public object GetInput(int index) => index == 0 ? source : predicate;

    public void Enter(in VectorRequest request) =>
        WhereDeviceState.ThrowIfCannotSelect(VectorResultCache.HasTarget(request.Target.FloatArray), Capacity,
            predicate);

    // An empty vector evaluates nothing
    public bool EvaluatesOperands(in VectorRequest request) => Capacity != 0;

    // The source and the predicate are read whole; the selection is cut to the window
    public VectorRequest OperandRequest(in VectorRequest request) => VectorRequest.Whole;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second)
    {
        if (Capacity == 0)
        {
            return VectorResult.Of(new FloatVectorEvaluation(VectorResultCache.EmptyFloat(workspace, WordsPerElement),
                DeviceElementCount.Static(0)));
        }

        var evaluatedSource = second.Float;
        var selected = _state.Select<FloatVectorResultKind, GpuBigFloatArrayView>(workspace, evaluatedSource.Storage,
            evaluatedSource.Count, first.Bool, Capacity, WordsPerElement, request.Offset, request.Count,
            out var count);
        return VectorResult.Of(new FloatVectorEvaluation(selected, count));
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
