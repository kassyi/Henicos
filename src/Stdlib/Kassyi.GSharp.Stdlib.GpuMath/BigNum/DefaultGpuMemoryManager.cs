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
/// <para>
/// <b>CUDA graphs:</b> a buffer freed while the workspace stream is captured stays out of the shared pool, because the
/// graph keeps using it on every launch (design 4.5.2). Inside a capture started by <c>CompileToGraph</c>
/// (<see cref="BeginDeferral"/>) it goes to the capture's own free list, from which later allocations of the same
/// capture take it in stream order; the buffers left there belong to the graph and come back to the pool when it is
/// disposed (<see cref="ReturnDeferred"/>). A buffer freed during a capture that the user started on the workspace
/// stream is parked until the workspace is disposed. <see cref="Dispose"/> frees every deferred buffer that has not
/// come back. Captures assume one thread per workspace and one stream (the capture is THREAD_LOCAL and is not forked).
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

    // Buffers freed during a capture and not yet returned: held by a graph, or parked until Dispose (design 4.5.2)
    private readonly ConcurrentDictionary<GpuBigIntDeviceBuffer, byte> _deferred = new();
    // The capture CompileToGraph is running on this workspace, if any
    private CaptureDeferral? _activeDeferral;

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

        var session = Volatile.Read(ref _activeDeferral);
        if (session?.TryTakeFree(numWords) is { } reused)
        {
            // Taken again in the same capture: it now belongs to its new owner, not to the graph's deferred set
            _deferred.TryRemove(reused, out _);
            return Lease(reused, session);
        }

        if (_tempPool.TryGetValue(numWords, out var lazyBag) && lazyBag.IsValueCreated &&
            lazyBag.Value.TryTake(out var pooled))
        {
            Interlocked.Decrement(ref _pooledBuffers);
            return Lease(pooled, session);
        }

        var ptr = provider.AllocateMemory(numWords);
        Interlocked.Increment(ref _driverAllocations);
        var buffer = new GpuBigIntDeviceBuffer(ptr, numWords, this);
        var handle = Lease(buffer, session);
        provider.ClearMemoryAsync(ptr, numWords, stream);
        return handle;
    }

    // Read the generation after taking the buffer: the free that released it advanced the generation before that
    private GpuBigIntMemoryHandle Lease(GpuBigIntDeviceBuffer buffer, CaptureDeferral? session)
    {
        _activeAllocations.TryAdd(buffer.Pointer, buffer);
        CountLease(buffer.TotalWords);
        session?.MarkLeased(buffer);
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

        // A graph being captured keeps using the buffer on every launch, so it must not reach the shared pool. The own
        // capture is checked first: THREAD_LOCAL capture is visible to the driver only on the capturing thread.
        var session = Volatile.Read(ref _activeDeferral);
        if (session is not null)
        {
            // The clear is recorded as a node of the graph, so the buffer is zero whenever a later node takes it
            provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, stream);
            _deferred.TryAdd(buffer, 0);
            session.AddFree(buffer);
            return;
        }

        if (IsStreamCapturing())
        {
            // A capture the user started: the graph is unknown here, so the buffer stays parked until Dispose
            _deferred.TryAdd(buffer, 0);
            return;
        }

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
    /// Starts deferring the frees of a capture that <c>CompileToGraph</c> is about to begin on the workspace stream.
    /// </summary>
    /// <returns>The deferral to pass to <see cref="EndDeferral"/>.</returns>
    /// <exception cref="InvalidOperationException">Another capture is already running on this workspace.</exception>
    internal CaptureDeferral BeginDeferral()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var deferral = new CaptureDeferral();
        if (Interlocked.CompareExchange(ref _activeDeferral, deferral, null) is not null)
        {
            throw new InvalidOperationException("A CUDA graph capture is already running on this workspace.");
        }

        return deferral;
    }

    /// <summary>
    /// Ends <paramref name="deferral"/> (after the capture ended, also when it failed).
    /// </summary>
    /// <param name="deferral">The deferral returned by <see cref="BeginDeferral"/>.</param>
    /// <returns>
    /// The buffers freed during the capture and not taken again, which now belong to the graph, and the handles the
    /// capture read that the graph does not own (operands and target), whose liveness the graph checks before a launch.
    /// </returns>
    internal CapturedResources EndDeferral(CaptureDeferral deferral)
    {
        Interlocked.CompareExchange(ref _activeDeferral, null, deferral);
        return deferral.Complete();
    }

    /// <summary>
    /// Gives back the buffers a graph held (when it is disposed, or when its capture failed).
    /// </summary>
    /// <param name="buffers">The buffers from <see cref="CapturedResources.Buffers"/>.</param>
    /// <remarks>
    /// A buffer that is no longer deferred (the workspace was disposed and freed it) is skipped, so a graph disposed
    /// after its workspace frees nothing twice.
    /// </remarks>
    internal void ReturnDeferred(GpuBigIntDeviceBuffer[] buffers)
    {
        foreach (var buffer in buffers)
        {
            if (!_deferred.TryRemove(buffer, out _))
            {
                continue;
            }

            provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, stream);
            var bag = _tempPool.GetOrAdd(buffer.TotalWords,
                static _ => new Lazy<ConcurrentBag<GpuBigIntDeviceBuffer>>(static () => [])).Value;
            bag.Add(buffer);
            Interlocked.Increment(ref _pooledBuffers);
        }

        if (Volatile.Read(ref _disposed) == 1)
        {
            DrainPool(null);
        }
    }

    /// <summary>
    /// Records that a capture read <paramref name="handle"/>, so that the graph can check it is still alive before each
    /// launch; called by <see cref="GpuBigIntMemoryHandle.DevicePointer"/>.
    /// </summary>
    /// <param name="handle">A live handle of this manager.</param>
    internal void ObserveCaptured(GpuBigIntMemoryHandle handle) => Volatile.Read(ref _activeDeferral)?.Observe(handle);

    // cuStreamIsCapturing for a capture that CompileToGraph did not start; there is no stream in CPU fallback mode
    private bool IsStreamCapturing()
    {
        if (stream == IntPtr.Zero || GpuKernelLoader.IsCpuFallbackMode)
        {
            return false;
        }

        // A query that fails (no current context) is treated as "not capturing", the state of every non-graph path
        return CudaDriverApi.cuStreamIsCapturing(stream, out var status) == CUresult.CudaSuccess &&
            status != CUstreamCaptureStatus.None;
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
        Volatile.Read(ref _driverFrees),
        _deferred.Count
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
    /// The deferred buffers of graphs that were not disposed are released with the immediate, device-wide free: a launch
    /// of such a graph may still be running, and the workspace destroys its stream right after this call without
    /// waiting for it (#659), so a stream-ordered free could leave the memory allocated.
    /// </remarks>
    [ExcludeFromCodeCoverage] public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var exceptions = new List<Exception>();
        DrainPool(exceptions);

        foreach (var deferred in _deferred.Keys)
        {
            // A graph disposed concurrently returns the buffer itself
            if (!_deferred.TryRemove(deferred, out _))
            {
                continue;
            }

            deferred.TryRetire(deferred.Generation);
            try
            {
                provider.FreeMemory(deferred.Pointer);
                Interlocked.Increment(ref _driverFrees);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

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

