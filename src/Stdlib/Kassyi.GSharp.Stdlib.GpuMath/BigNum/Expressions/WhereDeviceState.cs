namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The device buffers a <c>Where</c> node owns and reuses (<see cref="VectorWhereExpression"/>,
/// <see cref="FloatVectorWhereExpression"/>): the result cache (selected elements and the scan of the predicates), the
/// count word and the block sums of the scan. An evaluation allocates nothing after the first one of a capacity.
/// </summary>
internal sealed class WhereDeviceState : IDisposable
{
    private GpuBigUInt _count;
    private StreamCompactionOrchestrator.ScanScratch? _scratch;
    private int _scratchCapacity = -1;

    public VectorResultCache Cache { get; } = new();

    /// <summary>
    /// Checks what both Where nodes refuse before anything is evaluated: a target (the size of the result is known only
    /// on the device) and a predicate of another length than the vector.
    /// </summary>
    /// <exception cref="NotSupportedException">A target was passed.</exception>
    /// <exception cref="ArgumentException">The predicate's capacity differs from <paramref name="capacity"/>.</exception>
    public static void ThrowIfCannotSelect(bool hasTarget, int capacity, IGpuBoolVectorExpression predicate)
    {
        if (hasTarget)
        {
            throw new NotSupportedException(
                "Where expression does not support targetResult because its size is dynamic.");
        }

        if (predicate.Capacity != capacity)
        {
            throw new ArgumentException(
                $"The predicate has {predicate.Capacity} elements; the vector has {capacity}.", nameof(predicate));
        }
    }

    /// <summary>
    /// The steps of a Where after its source was evaluated: evaluates the predicate, scans its flags over the input
    /// count (a Where after another one reads only the elements the first one selected, design J4), moves the selected
    /// elements to their positions in the cache, and returns the window
    /// <paramref name="elementOffset"/>..<paramref name="elementCount"/> with its count.
    /// </summary>
    public TResult Select<TKind, TResult>(
        GpuWorkspace workspace,
        TResult source,
        DeviceElementCount sourceCount,
        IGpuBoolVectorExpression predicate,
        int capacity,
        int wordsPerElement,
        int elementOffset,
        int? elementCount,
        out DeviceElementCount count)
        where TKind : IVectorCompactKind<TResult>
        where TResult : struct, allows ref struct
    {
        var evalCount = elementCount ?? (capacity - elementOffset);
        var predicates = DeviceCounted.Evaluate(predicate, workspace);
        var inputCount = DeviceCounted.Combine(sourceCount, predicates.Count);

        // 1. The scan of the predicates gives each selected element its position, and its total is the count
        var scan = ScanFlags(workspace, predicates.Flags.DevicePointer, capacity, inputCount);

        // 2. The selected elements move to their positions (all components in one launch)
        var selected = TKind.Selected(Cache, workspace, capacity, wordsPerElement);
        StreamCompactionOrchestrator.Compact<TKind, TResult>(workspace, selected, source,
            predicates.Flags.DevicePointer, scan.DevicePointer, inputCount);

        count = Count(capacity).SkipFirst(elementOffset).TakeFirst(evalCount);
        return TKind.Slice(selected, elementOffset, evalCount);
    }

    /// <summary>
    /// Scans the predicate flags into the cached scan buffer, which gives each selected element its position, and
    /// writes their total (the number of selected elements) into the count word; returns the scan.
    /// </summary>
    private GpuBigUIntArrayView ScanFlags(GpuWorkspace workspace, IntPtr flags, int capacity,
        DeviceElementCount inputCount)
    {
        EnsureCountAndScratch(workspace, capacity);
        var scan = Cache.Scan(workspace, capacity);
        StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, scan.DevicePointer, flags, capacity,
            ScratchOrThrow(), inputCount, _count.DevicePointer, flags: true);
        return scan;
    }

    /// <summary>The number of selected elements written by the last <see cref="ScanFlags"/>, at most
    /// <paramref name="capacity"/>.</summary>
    private DeviceElementCount Count(int capacity) => new(_count.DevicePointer, 0, capacity);

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
            Cache.Dispose();
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
                _scratch?.Dispose();
                _scratch = null;
                _scratchCapacity = -1;
            }
        }
    }
}
