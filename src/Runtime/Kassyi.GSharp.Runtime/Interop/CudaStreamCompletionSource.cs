using System.Threading.Tasks.Sources;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A pooled, reusable <see cref="IValueTaskSource"/> that is signaled when all work enqueued on a CUDA stream has completed.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> <c>cuStreamSynchronize</c> parks the calling thread until the GPU drains the stream, which starves
/// thread pools in server workloads. This source instead enqueues a host function (<c>cuLaunchHostFunc</c>) behind the
/// pending work and completes a <see cref="ValueTask"/> from the driver's callback thread.
/// </para>
/// <para>
/// <b>Completion arbitration:</b> three parties may complete an armed operation — the host callback (success),
/// the cancellation callback, and the watchdog (stream or context fault). Only the party that wins the
/// compare-exchange on <see cref="_pendingVersion"/> signals the core, so late or stale signals are discarded.
/// </para>
/// <para>
/// <b>Lifetime invariant:</b> the driver holds <see cref="_selfHandle"/> until the host callback runs. An instance is
/// therefore returned to the pool only after <i>both</i> the consumer has called <see cref="GetResult"/> and the host
/// callback has run (<see cref="_outstanding"/> reaches zero). If a context fault prevents the callback from ever
/// running, the count never reaches zero and the instance is simply never reused.
/// </para>
/// <para>
/// <b>Completion hook:</b> an optional <see cref="IStreamCompletionHook"/> is invoked by the host callback before the
/// awaiter is signaled. It observes the real completion of the device work even when the awaiter has already been
/// detached by cancellation, which lets callers release device-written resources at the right time.
/// </para>
/// <para>
/// <b>No context-bound state:</b> the instance owns no CUDA objects. The watchdog polls the observed stream with
/// <c>cuStreamQuery</c> instead of a recorded event, because a pooled event would outlive the context that created it
/// (e.g. an isolated <c>GpuWorkspace</c> context) and become a dangling handle.
/// </para>
/// <para>
/// <b>Context lifetime:</b> the watchdog makes the observed context current, and a destroyed context handle crashes the
/// process inside the driver instead of returning an error. Two rules keep the handle alive while it is used:
/// a tick runs its pending check and its driver calls under <see cref="_tickLock"/>, and the winner of
/// <see cref="TryComplete"/> passes through that lock before signaling, so no tick is inside the driver once an awaiter
/// can resume (and, for example, destroy the context). Contexts destroyed while an operation is still armed must go
/// through <see cref="GpuKernelLoader.DestroyContext"/>, which abandons those operations first. An operation leaves the
/// armed registry only after that barrier, so an empty registry for a context means no tick is using it.
/// </para>
/// <para>
/// <b>Lock order:</b> <see cref="_tickLock"/> before <see cref="s_armedLock"/>; no path acquires them the other way.
/// </para>
/// </remarks>
internal sealed class CudaStreamCompletionSource : IValueTaskSource, IDisposable
{
    // ValueTask versions are 16-bit and may wrap negative, so the sentinel lies outside the short range.
    private const int NotArmed = int.MinValue;

    private static readonly TimeSpan s_watchdogPeriod = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan s_abandonTimeout = TimeSpan.FromSeconds(10);
    private static readonly CudaStreamCompletionSource?[] s_pool = new CudaStreamCompletionSource?[16];

    // Armed operations, so that a context about to be destroyed can find the ones still observing it.
    private static readonly Lock s_armedLock = new();
    private static readonly List<CudaStreamCompletionSource> s_armed = new(s_pool.Length);

    private readonly Lock _tickLock = new();
    private readonly Timer _watchdog;
    private ManualResetValueTaskSourceCore<bool> _core;
    private GCHandle _selfHandle;
    private IntPtr _stream;
    private IntPtr _context;
    private IStreamCompletionHook? _hook;
    private int _pendingVersion = NotArmed;
    private int _armedVersion;
    private int _outstanding;
    private CancellationTokenRegistration _cancellationRegistration;

    private CudaStreamCompletionSource()
    {
        _core.RunContinuationsAsynchronously = true;
        _selfHandle = GCHandle.Alloc(this);
        _watchdog = new Timer(static state =>
        {
            if (state is CudaStreamCompletionSource source)
            {
                source.OnWatchdogTick();
            }
        }, this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Rents a source from the pool, enqueues the completion signal on <paramref name="stream"/>, and returns the armed source.
    /// </summary>
    /// <param name="stream">The stream to observe. The caller must have the stream's context current.</param>
    /// <param name="context">The CUDA context that owns <paramref name="stream"/>; made current by the watchdog.</param>
    /// <param name="cancellationToken">
    /// A token that detaches the awaiter without affecting the stream. An already canceled token still arms the signal,
    /// so that <paramref name="hook"/> observes the completion of work enqueued before this call.
    /// </param>
    /// <param name="hook">An optional hook invoked on the driver thread when the stream reaches the signal.</param>
    /// <returns>The <see cref="ValueTask"/> that completes once the stream has drained.</returns>
    /// <exception cref="CudaException">The completion signal could not be enqueued; <paramref name="hook"/> will not be invoked.</exception>
    public static ValueTask Arm(IntPtr stream, IntPtr context, CancellationToken cancellationToken,
        IStreamCompletionHook? hook = null) =>
        Rent().ArmCore(stream, context, cancellationToken, hook);

    /// <inheritdoc/>
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    /// <inheritdoc/>
    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);

    /// <inheritdoc/>
    public void GetResult(short token)
    {
        if (token != _core.Version)
        {
            throw new InvalidOperationException("The ValueTask has already been consumed or belongs to a different operation.");
        }

        if (_core.GetStatus(token) == ValueTaskSourceStatus.Pending)
        {
            // Consuming early would release the consumer's reference while the operation is still armed.
            throw new InvalidOperationException("The operation has not completed yet.");
        }

        try
        {
            _core.GetResult(token);
        }
        finally
        {
            // Dispose (not Unregister) waits for an in-flight cancellation callback to finish touching the core.
            _cancellationRegistration.Dispose();
            Release();
        }
    }

    /// <summary>
    /// Releases the watchdog timer and the GC handle. Called only for instances that do not fit into the pool.
    /// </summary>
    /// <remarks>Safe on the driver's host-callback thread, because no CUDA API is called.</remarks>
    public void Dispose()
    {
        // Already disposed by GetResult before the instance reached this point; disposing a default registration is a no-op.
        _cancellationRegistration.Dispose();
        _watchdog.Dispose();
        _selfHandle.Free();
    }

    private static CudaStreamCompletionSource Rent()
    {
        for (var i = 0; i < s_pool.Length; i++)
        {
            var candidate = Interlocked.Exchange(ref s_pool[i], null);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return new CudaStreamCompletionSource();
    }

    private unsafe ValueTask ArmCore(IntPtr stream, IntPtr context, CancellationToken cancellationToken,
        IStreamCompletionHook? hook)
    {
        _stream = stream;
        _context = context;
        _hook = hook;

        var version = _core.Version;
        _armedVersion = version;
        Volatile.Write(ref _outstanding, 2); // consumer (GetResult) + driver (host callback)
        Volatile.Write(ref _pendingVersion, version);

        // Registered before the driver call so that nothing can throw once the driver owns a reference.
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationRegistration = cancellationToken.UnsafeRegister(static (state, token) =>
            {
                if (state is CudaStreamCompletionSource source)
                {
                    source.TryComplete(source._armedVersion, new OperationCanceledException(token));
                }
            }, this);
        }

        delegate* unmanaged<IntPtr, void> callback = &OnHostCallback;
        var result = CudaDriverApi.cuLaunchHostFunc(stream, (IntPtr)callback, GCHandle.ToIntPtr(_selfHandle));
        if (result != CUresult.CudaSuccess)
        {
            // Nothing was enqueued, so the instance is exclusively owned here once a concurrently running
            // cancellation callback has finished (Dispose waits for it).
            _cancellationRegistration.Dispose();
            ReturnToPool();
            throw new CudaException(result);
        }

        lock (s_armedLock)
        {
            // Checked under the lock: TryComplete unregisters under it too, so a concurrent completion either already
            // happened (skip) or will remove this entry.
            if (Volatile.Read(ref _pendingVersion) == version)
            {
                s_armed.Add(this);
                _watchdog.Change(s_watchdogPeriod, s_watchdogPeriod);
            }
        }

        return new ValueTask(this, version);
    }

    /// <summary>
    /// Faults every operation still armed on a stream of <paramref name="context"/> and stops their watchdogs.
    /// </summary>
    /// <param name="context">The context that is about to be destroyed.</param>
    /// <remarks>
    /// Returns only after no watchdog is inside a driver call for those operations, so the caller may destroy
    /// <paramref name="context"/> immediately afterwards.
    /// </remarks>
    /// <exception cref="TimeoutException">
    /// A watchdog tick did not leave the driver in time. The context must not be destroyed, because destroying it under
    /// an in-flight tick crashes the process.
    /// </exception>
    internal static void AbandonContext(IntPtr context) => AbandonContext(context, s_abandonTimeout);

    /// <inheritdoc cref="AbandonContext(IntPtr)"/>
    /// <param name="context">The context that is about to be destroyed.</param>
    /// <param name="timeout">The longest time to wait for an in-flight tick to leave the driver.</param>
    internal static void AbandonContext(IntPtr context, TimeSpan timeout)
    {
        var spinner = new SpinWait();
        var start = Stopwatch.GetTimestamp();
        while (TryTakeArmed(context, out var source, out var version))
        {
            if (!source.TryComplete(version, new ObjectDisposedException(
                    "CUDA context", "The CUDA context was destroyed while an operation was awaiting its stream.")))
            {
                // Another party won and unregisters the entry once its in-flight tick has left the driver.
                if (Stopwatch.GetElapsedTime(start) > timeout)
                {
                    throw new TimeoutException("A stream completion watchdog tick did not leave the CUDA driver; the context was not destroyed.");
                }

                spinner.SpinOnce();
            }
        }
    }

    /// <summary>
    /// Returns the tick barrier of an operation still armed on <paramref name="context"/>, so that tests can simulate an
    /// in-flight watchdog tick.
    /// </summary>
    internal static Lock? FindTickLock(IntPtr context)
    {
        lock (s_armedLock)
        {
            foreach (var candidate in s_armed)
            {
                if (Volatile.Read(ref candidate._context) == context)
                {
                    return candidate._tickLock;
                }
            }
        }

        return null;
    }

    private static bool TryTakeArmed(IntPtr context, [NotNullWhen(true)] out CudaStreamCompletionSource? source,
        out int version)
    {
        lock (s_armedLock)
        {
            foreach (var candidate in s_armed)
            {
                if (Volatile.Read(ref candidate._context) != context)
                {
                    continue;
                }

                // The winning TryComplete removes the entry, so the loop in AbandonContext ends once all winners pass.
                source = candidate;
                version = Volatile.Read(ref candidate._armedVersion);
                return true;
            }
        }

        source = null;
        version = NotArmed;
        return false;
    }

    [UnmanagedCallersOnly]
    private static void OnHostCallback(IntPtr userData)
    {
        // Runs on a CUDA driver thread: must not call any CUDA API and must not throw across the native boundary.
        // The driver only invokes this after all preceding work succeeded; context faults are left to the watchdog.
        if (GCHandle.FromIntPtr(userData).Target is not CudaStreamCompletionSource source)
        {
            return;
        }

        try
        {
            // Before signaling, so that an awaiter resuming on the success path already observes the hook's effects.
            source._hook?.OnStreamCompleted();
            source.TryComplete(source._armedVersion, error: null);
        }
        catch (Exception ex)
        {
            source.TryComplete(source._armedVersion, ex);
        }
        finally
        {
            source.Release();
        }
    }

    private void OnWatchdogTick()
    {
        // Held across the pending check and the driver calls; see "Context lifetime" in the type remarks.
        // TryComplete below re-enters it on this thread, which Lock permits.
        lock (_tickLock)
        {
            var version = Volatile.Read(ref _armedVersion);
            if (Volatile.Read(ref _pendingVersion) != version)
            {
                return;
            }

            var context = Volatile.Read(ref _context);
            var stream = Volatile.Read(ref _stream);

            CUresult status;
            try
            {
                using var scope = new GpuKernelLoader.CudaContextScope(context);

                // Also observes work enqueued after the completion signal. That is harmless because only fault codes
                // are acted upon; success is always left to the host callback, which owns the reuse handshake.
                status = CudaDriverApi.cuStreamQuery(stream);
            }
            catch (CudaException ex)
            {
                // The owning context can no longer be made current; surface it instead of hanging.
                TryComplete(version, ex);
                return;
            }

            if (status is CUresult.CudaSuccess or CUresult.CudaErrorNotReady)
            {
                return;
            }

            TryComplete(version, GpuKernelLoader.CreateExceptionForDriverError(status, $"CUDA stream faulted while awaiting completion: {status}"));
        }
    }

    private bool TryComplete(int version, Exception? error)
    {
        if (Interlocked.CompareExchange(ref _pendingVersion, NotArmed, version) != version)
        {
            return false;
        }

        // Waits for a tick that passed its pending check before the exchange above to leave the driver. Ticks that
        // start later see the exchange and return without touching the context.
        lock (_tickLock)
        {
            // Unregistered only past that barrier: AbandonContext treats an absent entry as "no tick can be in the
            // driver", so removing it earlier would let cuCtxDestroy run under an in-flight tick.
            lock (s_armedLock)
            {
                s_armed.Remove(this);
            }

            // After the s_armedLock section, so that a concurrent ArmCore cannot restart the timer after this stop.
            _watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        if (error is null)
        {
            _core.SetResult(true);
        }
        else
        {
            _core.SetException(error);
        }

        return true;
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _outstanding) == 0)
        {
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        _watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Volatile.Write(ref _pendingVersion, NotArmed);
        _cancellationRegistration = default;
        Volatile.Write(ref _stream, IntPtr.Zero);
        Volatile.Write(ref _context, IntPtr.Zero);
        _hook = null;
        _core.Reset();

        for (var i = 0; i < s_pool.Length; i++)
        {
            if (Interlocked.CompareExchange(ref s_pool[i], this, null) is null)
            {
                return;
            }
        }

        // Pool is full. No CUDA objects are owned, so teardown is safe even on the driver's host-callback thread.
        Dispose();
    }
}
