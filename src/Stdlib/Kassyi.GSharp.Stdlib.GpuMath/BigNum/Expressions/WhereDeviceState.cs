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
    /// The steps of a Where after its predicate and its source were evaluated: scans the predicate flags over the input
    /// count (a Where after another one reads only the elements the first one selected, design J4), moves the selected
    /// elements to their positions in the cache, and returns the window
    /// <paramref name="elementOffset"/>..<paramref name="elementCount"/> with its count.
    /// </summary>
    public TResult Select<TKind, TResult>(
        GpuWorkspace workspace,
        TResult source,
        DeviceElementCount sourceCount,
        scoped BoolVectorEvaluation predicates,
        int capacity,
        int wordsPerElement,
        int elementOffset,
        int? elementCount,
        out DeviceElementCount count,
        out GpuBigUIntArrayView scan)
        where TKind : IVectorCompactKind<TResult>
        where TResult : struct, allows ref struct
    {
        var evalCount = elementCount ?? (capacity - elementOffset);
        var inputCount = DeviceCounted.Combine(sourceCount, predicates.Count);

        // 1. The scan of the predicates gives each selected element its position, and its total is the count
        scan = ScanFlags(workspace, predicates.Flags.DevicePointer, capacity, inputCount);

        // 2. The selected elements move to their positions (all components in one launch)
        var selected = TKind.Selected(Cache, workspace, capacity, wordsPerElement);
        StreamCompactionOrchestrator.Compact<TKind, TResult>(workspace, selected, source,
            predicates.Flags.DevicePointer, scan.DevicePointer, inputCount);

        count = Count(capacity).SkipFirst(elementOffset).TakeFirst(evalCount);
        return TKind.Slice(selected, elementOffset, evalCount);
    }

    /// <summary>
    /// Closes the source and predicate index spaces opened by <see cref="IIndexSpaceBoundary.BeginOperand"/>, drops the
    /// failures C# never reaches, picks the earlier remaining one in C# order (source before predicate at the same
    /// input index), translates it into the requested window, and records it if it falls inside the window.
    /// </summary>
    /// <remarks>
    /// C# pairs the source with the predicate as <c>Zip</c> does: element <c>i</c> evaluates the source first, then the
    /// predicate, and the pairing ends at the first input without an element <c>i</c>. So a source element runs up to
    /// and including the predicate's length (at that index the source still runs before the predicate ends), and a
    /// predicate element only below the source's length. The lengths are read only when a failure was recorded.
    /// </remarks>
    public void RecordTranslated(
        GpuWorkspace workspace,
        DeviceElementCount sourceCount,
        DeviceElementCount predicateCount,
        scoped GpuBigUIntArrayView scan,
        int offset,
        int evalCount)
    {
        var fromSource = PendingThrows.CloseSpace();
        var fromPredicate = PendingThrows.CloseSpace();
        if (fromSource.Exception is null && fromPredicate.Exception is null)
        {
            return;
        }

        var sourceLength = sourceCount.Read(workspace);
        var predicateLength = predicateCount.Read(workspace);
        if (fromSource.Index > predicateLength)
        {
            fromSource = PendingThrows.Recorded.Empty;
        }

        if (fromPredicate.Index >= sourceLength)
        {
            fromPredicate = PendingThrows.Recorded.Empty;
        }

        var recorded = fromSource.Exception is not null && fromSource.Index <= fromPredicate.Index
            ? fromSource
            : fromPredicate;
        var inputLength = Math.Min(sourceLength, predicateLength);
        if (recorded.Exception is { } exception
            && TranslateRecorded(workspace, recorded.Index, inputLength, scan, offset, evalCount) is { } index)
        {
            PendingThrows.RecordOrThrow(index, exception);
        }
    }

    /// <summary>
    /// Maps an input element index to the requested output window
    /// [<paramref name="offset"/>, <paramref name="offset"/> + <paramref name="evalCount"/>), clamping skipped
    /// elements to 0 and returning <see langword="null"/> past the window.
    /// </summary>
    public int? TranslateRecorded(
        GpuWorkspace workspace,
        int index,
        int inputLength,
        scoped GpuBigUIntArrayView scan,
        int offset,
        int evalCount)
    {
        var n = inputLength;
        var mapped = index < n
            ? workspace.Provider.ReadWord(scan.DevicePointer, index, workspace.Stream)
            : workspace.Provider.ReadWord(_count.DevicePointer, 0, workspace.Stream);
        var j = checked((int)mapped) - offset;
        if (j < 0)
        {
            j = 0;
        }

        return j >= evalCount ? null : j;
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
