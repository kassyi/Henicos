namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Keeps the elements whose predicate is true (design D3 4.4). The number of selected elements is computed on the
/// device into a word this node owns, so neither the selection nor its consumers (Count, Sum, Skip, ...) synchronize
/// with the host; only the public <see cref="Evaluate"/> reads it, to return exactly the selected elements.
/// </summary>
internal class VectorWhereExpression(
    [TakesOwnership] IGpuBigIntVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    public int Length => throw new InvalidOperationException(DeviceCounted.LengthUnknown);

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

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    public VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            throw new NotSupportedException(
                "Where expression does not support targetResult because its size is dynamic.");
        }

        var capacity = Capacity;
        if (predicate.Capacity != capacity)
        {
            throw new ArgumentException(
                $"The predicate has {predicate.Capacity} elements; the vector has {capacity}.", nameof(predicate));
        }

        var evalCount = elementCount ?? (capacity - elementOffset);
        if (capacity == 0)
        {
            return new VectorEvaluation(VectorResultCache.EmptySigned(workspace, WordsPerElement),
                DeviceElementCount.Static(0));
        }

        var evaluatedSource = DeviceCounted.Evaluate(source, workspace);
        var evaluatedPredicates = DeviceCounted.Evaluate(predicate, workspace);

        // The input count: a Where after another one reads only the elements the first one selected (design J4)
        var inputCount = DeviceCounted.Combine(evaluatedSource.Count, evaluatedPredicates.Count);
        var scanCount = new StreamCompactionOrchestrator.ScanCount(inputCount.Base, inputCount.Skip, inputCount.Limit);

        // 1. The scan of the predicates gives each selected element its position, and its total is the count
        var scan = _state.ScanFlags(workspace, evaluatedPredicates.Flags.DevicePointer, capacity, scanCount);

        // 2. The selected elements move to their positions (values and signs in one launch)
        var selected = new GpuBigIntArrayView(
            _state.Cache.Values(workspace, capacity, WordsPerElement), _state.Cache.Signs(workspace, capacity));
        StreamCompactionOrchestrator.CompactSignedVector(workspace, selected, evaluatedSource.Storage,
            evaluatedPredicates.Flags.DevicePointer, scan.DevicePointer, scanCount);

        var count = _state.Count(capacity);
        return new VectorEvaluation(selected.Slice(elementOffset, evalCount),
            count.SkipFirst(elementOffset).TakeFirst(evalCount));
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
