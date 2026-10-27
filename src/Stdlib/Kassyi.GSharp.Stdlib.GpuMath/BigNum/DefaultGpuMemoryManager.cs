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
/// <para>
/// <b>Double free:</b> the wrappers are value types, so two copies of one handle can both be freed. The pool keeps the
/// buffer objects, and each lease of a buffer has a generation (<see cref="GpuBigIntDeviceBuffer.TryRetire"/>): only the
/// first free of a lease returns the buffer, and a later free of a stale copy does nothing, even after the buffer has
/// been handed to a new owner. <see cref="Dispose"/> ends every lease the same way, so a value disposed after its
/// workspace never frees device memory a second time.
/// </para>
/// </remarks>
internal sealed class DefaultGpuMemoryManager(IGpuExecutionProvider provider, IntPtr stream)
    : IGpuMemoryManager, IDisposable
{
    // Free buffers per word count; a handle is created for each lease, so a sub-range handle never enters the pool
    private readonly ConcurrentDictionary<int, Lazy<ConcurrentBag<GpuBigIntDeviceBuffer>>> _tempPool = new();
    // Leased buffers, released by Dispose; whoever removes a buffer from here releases it
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
    /// <exception cref="ObjectDisposedException">The manager (its workspace) has been disposed.</exception>
    public GpuBigIntMemoryHandle Allocate(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (numWords == 0)
        {
            return default;
        }

        if (_tempPool.TryGetValue(numWords, out var lazyBag) && lazyBag.IsValueCreated &&
            lazyBag.Value.TryTake(out var pooled))
        {
            Interlocked.Decrement(ref _pooledBuffers);
            _activeAllocations.TryAdd(pooled.Pointer, pooled);
            CountLease(pooled.TotalWords);
            // Read after TryTake: the free that pooled the buffer advanced the generation before adding it
            return GpuBigIntMemoryHandle.CreateOwned(pooled);
        }

        var ptr = provider.AllocateMemory(numWords);
        Interlocked.Increment(ref _driverAllocations);
        var buffer = new GpuBigIntDeviceBuffer(ptr, numWords, this);
        _activeAllocations.TryAdd(ptr, buffer);
        CountLease(numWords);
        provider.ClearMemoryAsync(ptr, numWords, stream);
        return GpuBigIntMemoryHandle.CreateOwned(buffer);
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
    /// Ends the lease of the buffer of <paramref name="handle"/> and returns the buffer to the pool.
    /// </summary>
    /// <param name="handle">
    /// The owning handle of a whole buffer, as returned by <see cref="Allocate"/>. A borrowed handle (a view or a slice)
    /// is rejected even when its lease has already ended.
    /// </param>
    /// <remarks>
    /// Freeing a default handle, a handle of external memory, or a handle whose lease has already ended (a second free of
    /// a copy, or a free after <see cref="Dispose"/>) does nothing.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="handle"/> was allocated by another memory manager.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="handle"/> is borrowed or covers only part of its buffer; only the owning handle of a whole buffer
    /// can be freed. The public <c>Dispose</c> methods never pass such a handle, so this is an error inside GpuMath.
    /// </exception>
    public void Free(GpuBigIntMemoryHandle handle)
    {
        if (handle.IsDefault || handle.Buffer.Manager is null)
        {
            return;
        }

        if (!ReferenceEquals(handle.Buffer.Manager, this))
        {
            throw new ArgumentException("The handle was allocated by the memory manager of another workspace.",
                nameof(handle));
        }

        var buffer = handle.Buffer;
        if (!handle.OwnsBuffer || handle.WordOffset != 0 || handle.NumWords != buffer.TotalWords)
        {
            throw new InvalidOperationException(
                "Only the owning handle of a whole buffer can be freed; a view or slice is borrowed.");
        }

        if (!buffer.TryRetire(handle.Generation) || !_activeAllocations.TryRemove(buffer.Pointer, out _))
        {
            return;
        }

        CountRelease(buffer.TotalWords);
        provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, stream);

        // Return to pool based on total size
        var bag = _tempPool.GetOrAdd(buffer.TotalWords,
            static _ => new Lazy<ConcurrentBag<GpuBigIntDeviceBuffer>>(static () => [])).Value;
        bag.Add(buffer);
        Interlocked.Increment(ref _pooledBuffers);

        // A Dispose that ran concurrently may already have drained the pool; release what it could not see
        if (Volatile.Read(ref _disposed) == 1)
        {
            DrainPool(null);
        }
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

    // Retires and frees every pooled buffer. Without a list (a Free that raced with Dispose) a failure is rethrown to
    // that caller, since the workspace stream may already be destroyed and nothing else would report it.
    private void DrainPool(List<Exception>? exceptions)
    {
        foreach (var lazyBag in _tempPool.Values)
        {
            if (!lazyBag.IsValueCreated)
            {
                continue;
            }

            while (lazyBag.Value.TryTake(out var pooled))
            {
                Interlocked.Decrement(ref _pooledBuffers);
                pooled.TryRetire(pooled.Generation);
                try
                {
                    provider.FreeMemory(pooled.Pointer, stream);
                    Interlocked.Increment(ref _driverFrees);
                }
                catch (Exception ex) when (exceptions is not null)
                {
                    exceptions.Add(ex);
                }
            }
        }
    }

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
    /// Every lease is ended before its buffer is released, so a handle that survives the manager reads as freed
    /// (<see cref="ObjectDisposedException"/>) and freeing it does nothing.
    /// </remarks>
    [ExcludeFromCodeCoverage] public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var exceptions = new List<Exception>();
        DrainPool(exceptions);

        foreach (var ptr in _activeAllocations.Keys)
        {
            // A concurrent Free that removed the buffer first releases it itself
            if (!_activeAllocations.TryRemove(ptr, out var live))
            {
                continue;
            }

            // End the lease first: a surviving copy of the handle must never reach FreeMemory again
            live.TryRetire(live.Generation);
            CountRelease(live.TotalWords);
            try
            {
                provider.FreeMemory(ptr, stream);
                Interlocked.Increment(ref _driverFrees);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException("Errors occurred during GPU memory disposal.", exceptions);
        }
    }
}

