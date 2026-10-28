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

    // The selected elements and the scan of the predicates (grown to the capacity), the count word, and the block sums
    // of the scan: all owned here and reused, so that an evaluation allocates nothing after the first
    private readonly VectorResultCache _cache = new();
    private GpuBigUInt _count;
    private StreamCompactionOrchestrator.ScanScratch? _scratch;
    private int _scratchCapacity = -1;

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

        EnsureCountAndScratch(workspace, capacity);
        var scan = _cache.Scan(workspace, capacity);
        var selected = new GpuBigIntArrayView(
            _cache.Values(workspace, capacity, WordsPerElement), _cache.Signs(workspace, capacity));

        // 1. The scan of the predicates gives each selected element its position, and its total is the count
        StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, scan.DevicePointer,
            evaluatedPredicates.Flags.DevicePointer, capacity, ScratchOrThrow(), scanCount, _count.DevicePointer,
            flags: true);

        // 2. The selected elements move to their positions (values and signs in one launch)
        StreamCompactionOrchestrator.CompactSignedVector(workspace, selected, evaluatedSource.Storage,
            evaluatedPredicates.Flags.DevicePointer, scan.DevicePointer, scanCount);

        var count = new DeviceElementCount(_count.DevicePointer, 0, capacity);
        return new VectorEvaluation(selected.Slice(elementOffset, evalCount),
            count.SkipFirst(elementOffset).TakeFirst(evalCount));
    }

    private void EnsureCountAndScratch(GpuWorkspace workspace, int capacity)
    {
        if (_count.Handle.IsDefault)
        {
            _count = new GpuBigUInt(workspace, 1);
        }

        if (_scratchCapacity == capacity)
        {
            return;
        }

        var scratch = new StreamCompactionOrchestrator.ScanScratch(workspace, capacity);
        _scratch?.Dispose();
        _scratch = scratch;
        _scratchCapacity = capacity;
    }

    private StreamCompactionOrchestrator.ScanScratch ScratchOrThrow() =>
        _scratch ?? throw new InvalidOperationException("The scan scratch was not allocated.");

    public void Dispose()
    {
        // A release that throws does not skip the others
        try
        {
            _cache.Dispose();
        }
        finally
        {
            try
            {
                _count.Dispose();
                _count = default;
            }
            finally
            {
                try
                {
                    if (_scratch != null)
                    {
                        _scratch.Dispose();
                    }

                    _scratch = null;
                    _scratchCapacity = -1;
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
    }
}
