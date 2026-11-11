namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Keeps the elements whose predicate is true. The number of selected elements is computed on the
/// device into a word this node owns, so neither the selection nor its consumers (Count, Sum, Skip, ...) synchronize
/// with the host; only the public <c>Evaluate</c> reads it, to return exactly the selected elements.
/// </summary>
internal class VectorWhereExpression(
    [TakesOwnership] IGpuBigIntVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity => source.Capacity;

    public int WordsPerElement => source.WordsPerElement;
    public long BitsPerElement => source.BitsPerElement;

    // The selected elements, the scan of the predicates, the count word and the block sums of the scan: owned here and
    // reused, so that an evaluation allocates nothing after the first
    private readonly WhereDeviceState _state = new();

    public bool TryGetLength(out int length)
    {
        length = 0;
        return false;
    }

    public VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        var capacity = Capacity;
        WhereDeviceState.ThrowIfCannotSelect(VectorResultCache.HasTarget(targetResult), capacity, predicate);
        if (capacity == 0)
        {
            return new VectorEvaluation(VectorResultCache.EmptySigned(workspace, WordsPerElement),
                DeviceElementCount.Static(0));
        }

        // The predicate first: when the source and the predicate read one shared sub-expression at different offsets
        // (e.Skip(1).Where(e.Take(3).IsGreaterThan(x))), the later evaluation overwrites that node's cache. The flags
        // are in the predicate's own buffer, which a source evaluation leaves as it is (a bool vector is always read
        // over its whole length, so evaluating a shared bool node again writes the same flags); the source evaluated
        // second keeps its values.
        var predicates = DeviceCounted.Evaluate(predicate, workspace);
        var evaluatedSource = DeviceCounted.Evaluate(source, workspace);
        var selected = _state.Select<IntVectorResultKind, GpuBigIntArrayView>(workspace, evaluatedSource.Storage,
            evaluatedSource.Count, predicates, capacity, WordsPerElement, elementOffset, elementCount, out var count);
        return new VectorEvaluation(selected, count);
    }

    public void Dispose()
    {
        // A release that throws does not skip the others
        try
        {
            _state.Dispose();
        }
        finally
        {
            try
            {
                source.Dispose();
            }
            finally
            {
                predicate.Dispose();
            }
        }
    }
}
