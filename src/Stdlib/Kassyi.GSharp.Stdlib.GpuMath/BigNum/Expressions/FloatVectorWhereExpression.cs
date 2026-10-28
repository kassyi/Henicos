namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The floating-point <c>Where</c> (design D3 4.4): the number of selected elements is computed on the device into a
/// word this node owns, as in <see cref="VectorWhereExpression"/>.
/// </summary>
internal sealed class FloatVectorWhereExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    public int Length => throw new InvalidOperationException(DeviceCounted.LengthUnknown);

    public int Capacity => source.Capacity;

    public int WordsPerElement => source.WordsPerElement;

    // The selected elements and the scan of the predicates (grown to the capacity), the count word, and the block sums
    // of the scan: all owned here and reused
    private readonly VectorResultCache _cache = new();
    private GpuBigUInt _count;
    private StreamCompactionOrchestrator.ScanScratch? _scratch;
    private int _scratchCapacity = -1;

    public bool TryGetLength(out int length)
    {
        length = 0;
        return false;
    }

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            throw new NotSupportedException("Where expression does not support targetResult because its size is dynamic.");
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
            return new FloatVectorEvaluation(VectorResultCache.EmptyFloat(workspace, WordsPerElement),
                DeviceElementCount.Static(0));
        }

        var evaluatedSource = DeviceCounted.Evaluate(source, workspace);
        var evaluatedPredicates = DeviceCounted.Evaluate(predicate, workspace);
        var inputCount = DeviceCounted.Combine(evaluatedSource.Count, evaluatedPredicates.Count);
        var scanCount = new StreamCompactionOrchestrator.ScanCount(inputCount.Base, inputCount.Skip, inputCount.Limit);

        EnsureCountAndScratch(workspace, capacity);
        var scan = _cache.Scan(workspace, capacity);
        var selected = _cache.Float(workspace, capacity, WordsPerElement);

        StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, scan.DevicePointer,
            evaluatedPredicates.Flags.DevicePointer, capacity, ScratchOrThrow(), scanCount, _count.DevicePointer,
            flags: true);
        StreamCompactionOrchestrator.CompactFloatVector(workspace, selected, evaluatedSource.Storage,
            evaluatedPredicates.Flags.DevicePointer, scan.DevicePointer, scanCount);

        var count = new DeviceElementCount(_count.DevicePointer, 0, capacity);
        return new FloatVectorEvaluation(selected.Slice(elementOffset, evalCount),
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
        if (_scratch != null)
        {
            _scratch.Dispose();
        }

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
