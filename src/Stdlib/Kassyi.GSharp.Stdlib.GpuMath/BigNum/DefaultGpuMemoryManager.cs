using System.Collections.Concurrent;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides a pooled device memory manager implementation for GPU large-integer operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Frequent small device allocations (<c>cuMemAlloc</c>) introduce significant driver overhead and host-device synchronization stalls.
/// <see cref="DefaultGpuMemoryManager"/> maintains size-segregated concurrent pools of reusable device buffers to minimize allocation latency.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Thread-safe across concurrent GPU stream staging and disposal operations.
/// </para>
/// </remarks>
internal sealed class DefaultGpuMemoryManager(IGpuExecutionProvider provider, IntPtr stream)
    : IGpuMemoryManager, IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<ConcurrentBag<GpuBigIntMemoryHandle>>> _tempPool = new();
    private readonly ConcurrentDictionary<IntPtr, GpuBigIntDeviceBuffer> _activeAllocations = new();
    private int _disposed;

    // Allocation counters (GetStatistics); updated with Interlocked, read with Volatile
    private int _liveBuffers;
    private long _liveWords;
    private int _peakLiveBuffers;
    private int _pooledBuffers;
    private long _driverAllocations;
    private long _driverFrees;

    /// <summary>
    /// Allocates or reuses a device memory handle for the specified word count.
    /// </summary>
    /// <param name="numWords">The number of 32-bit limbs to allocate.</param>
    /// <returns>A <see cref="GpuBigIntMemoryHandle"/> referencing the device buffer.</returns>
    public GpuBigIntMemoryHandle Allocate(int numWords)
    {
        if (numWords == 0)
        {
            return default;
        }

        if (_tempPool.TryGetValue(numWords, out var lazyBag) && lazyBag.IsValueCreated &&
            lazyBag.Value.TryTake(out var temp))
        {
            Interlocked.Decrement(ref _pooledBuffers);
            _activeAllocations.TryAdd(temp.Buffer.Pointer, temp.Buffer);
            CountLease(temp.Buffer.TotalWords);
            return temp;
        }

        var ptr = provider.AllocateMemory(numWords);
        Interlocked.Increment(ref _driverAllocations);
        var buffer = new GpuBigIntDeviceBuffer(ptr, numWords);
        _activeAllocations.TryAdd(ptr, buffer);
        CountLease(numWords);
        provider.ClearMemoryAsync(ptr, numWords, stream);
        return new GpuBigIntMemoryHandle(buffer, 0, numWords);
    }

    /// <summary>
    /// Allocates a temporary, scope-bound device memory handle.
    /// </summary>
    /// <param name="numWords">The required word count.</param>
    /// <returns>A <see cref="ScopedBigIntMemory"/> managing the scoped allocation.</returns>
    public ScopedBigIntMemory AllocateTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        var handle = Allocate(numWords);
        return new ScopedBigIntMemory(this, handle);
    }

    /// <summary>
    /// Recycles the specified memory handle back into the internal pool or releases it if disposed.
    /// </summary>
    /// <param name="handle">The memory handle to free or recycle.</param>
    public void Free(GpuBigIntMemoryHandle handle)
    {
        if (handle.IsDefault)
        {
            return;
        }

        // Prevent double frees which cause InvalidValue and Access Violation on CUDA
        if (handle.Buffer.Pointer != IntPtr.Zero && !_activeAllocations.TryRemove(handle.Buffer.Pointer, out _))
        {
            return;
        }

        if (Volatile.Read(ref _disposed) == 1)
        {
            // Already disposed, actually free it. The workspace stream may already be destroyed,
            // so the release cannot be stream-ordered and falls back to the immediate, device-wide free.
            if (handle.Buffer.Pointer == IntPtr.Zero)
            {
                return;
            }

            provider.FreeMemory(handle.Buffer.Pointer);
            Interlocked.Increment(ref _driverFrees);
            CountRelease(handle.Buffer.TotalWords);
            handle.Buffer.Dispose();
            return;
        }

        if (handle.Buffer.Pointer != IntPtr.Zero)
        {
            CountRelease(handle.Buffer.TotalWords);
            provider.ClearMemoryAsync(handle.Buffer.Pointer, handle.Buffer.TotalWords, stream);
        }

        // Return to pool based on total size
        var bag = _tempPool.GetOrAdd(handle.Buffer.TotalWords,
            _ => new Lazy<ConcurrentBag<GpuBigIntMemoryHandle>>(() => [])).Value;
        bag.Add(handle);
        Interlocked.Increment(ref _pooledBuffers);
    }

    /// <summary>
    /// Reads the allocation counters.
    /// </summary>
    /// <returns>A snapshot of the counters; see <see cref="GpuMemoryStatistics"/>.</returns>
    internal GpuMemoryStatistics GetStatistics() => new(
        Volatile.Read(ref _liveBuffers),
        Volatile.Read(ref _liveWords),
        Volatile.Read(ref _peakLiveBuffers),
        Volatile.Read(ref _pooledBuffers),
        Volatile.Read(ref _driverAllocations),
        Volatile.Read(ref _driverFrees)
    );

    private void CountLease(int numWords)
    {
        var live = Interlocked.Increment(ref _liveBuffers);
        Interlocked.Add(ref _liveWords, numWords);
        var peak = Volatile.Read(ref _peakLiveBuffers);
        while (live > peak)
        {
            var observed = Interlocked.CompareExchange(ref _peakLiveBuffers, live, peak);
            if (observed == peak)
            {
                break;
            }

            peak = observed;
        }
    }

    private void CountRelease(int numWords)
    {
        Interlocked.Decrement(ref _liveBuffers);
        Interlocked.Add(ref _liveWords, -numWords);
    }

    /// <summary>
    /// Releases all pooled device buffers and unmanaged resources.
    /// </summary>
    /// <remarks>
    /// Buffers are released in stream order on the workspace stream, which every buffer of this manager is used on.
    /// The calling thread therefore neither waits for that stream nor for unrelated streams of the device.
    /// The stream must not be capturing a CUDA Graph at this point.
    /// </remarks>
    [ExcludeFromCodeCoverage] public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var exceptions = new List<Exception>();

        foreach (var lazyBag in _tempPool.Values)
        {
            if (!lazyBag.IsValueCreated)
            {
                continue;
            }

            while (lazyBag.Value.TryTake(out var handle))
            {
                Interlocked.Decrement(ref _pooledBuffers);
                if (handle.Buffer.Pointer == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    provider.FreeMemory(handle.Buffer.Pointer, stream);
                    Interlocked.Increment(ref _driverFrees);
                    handle.Buffer.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }

        _tempPool.Clear();

        foreach (var (ptr, buffer) in _activeAllocations)
        {
            if (ptr == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                CountRelease(buffer.TotalWords);
                provider.FreeMemory(ptr, stream);
                Interlocked.Increment(ref _driverFrees);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        _activeAllocations.Clear();

        if (exceptions.Count > 0)
        {
            throw new AggregateException("Errors occurred during GPU memory disposal.", exceptions);
        }
    }
}

