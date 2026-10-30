namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Keeps the elements whose predicate is true (design D3 4.4). The number of selected elements is computed on the
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

        var evaluatedSource = DeviceCounted.Evaluate(source, workspace);
        var selected = _state.Select<IntVectorResultKind, GpuBigIntArrayView>(workspace, evaluatedSource.Storage,
            evaluatedSource.Count, predicate, capacity, WordsPerElement, elementOffset, elementCount, out var count);
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
