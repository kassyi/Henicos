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
    /// Scans the predicate flags into the cached scan buffer, which gives each selected element its position, and
    /// writes their total (the number of selected elements) into the count word; returns the scan.
    /// </summary>
    public GpuBigUIntArrayView ScanFlags(GpuWorkspace workspace, IntPtr flags, int capacity,
        StreamCompactionOrchestrator.ScanCount scanCount)
    {
        EnsureCountAndScratch(workspace, capacity);
        var scan = Cache.Scan(workspace, capacity);
        StreamCompactionOrchestrator.ExecuteExclusiveScan(workspace, scan.DevicePointer, flags, capacity,
            ScratchOrThrow(), scanCount, _count.DevicePointer, flags: true);
        return scan;
    }

    /// <summary>The number of selected elements written by the last <see cref="ScanFlags"/>, at most
    /// <paramref name="capacity"/>.</summary>
    public DeviceElementCount Count(int capacity) => new(_count.DevicePointer, 0, capacity);

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
