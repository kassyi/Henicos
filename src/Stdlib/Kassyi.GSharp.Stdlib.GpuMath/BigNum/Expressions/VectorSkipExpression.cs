namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorSkipExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _source = VectorShape.Of(source);

    // Skipping past the capacity skips everything; a device count below the skip gives 0 through SkipFirst
    private readonly int _skipCount = Math.Min(count, source.Capacity);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(source);

    // A slice adds no check; a projection outside it is not evaluated, as System.Linq
    public bool MayThrow => _inputsMayThrow;

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
