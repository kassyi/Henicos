using System.Threading.Tasks.Sources;
using Kassyi.GSharp.Runtime.Aot;

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
/// <b>No context-bound state:</b> the instance owns no CUDA objects. The watchdog polls the observed stream with
/// <c>cuStreamQuery</c> instead of a recorded event, because a pooled event would outlive the context that created it
/// (e.g. an isolated <c>GpuWorkspace</c> context) and become a dangling handle.
/// </para>
/// </remarks>
internal sealed class CudaStreamCompletionSource : IValueTaskSource, IDisposable
{
    // ValueTask versions are 16-bit and may wrap negative, so the sentinel lies outside the short range.
    private const int NotArmed = int.MinValue;

    private static readonly TimeSpan s_watchdogPeriod = TimeSpan.FromMilliseconds(100);
    private static readonly CudaStreamCompletionSource?[] s_pool = new CudaStreamCompletionSource?[16];

    private readonly Timer _watchdog;
    private ManualResetValueTaskSourceCore<bool> _core;
    private GCHandle _selfHandle;
    private IntPtr _stream;
    private IntPtr _context;
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
    /// <param name="cancellationToken">A token that detaches the awaiter without affecting the stream.</param>
    /// <returns>The <see cref="ValueTask"/> that completes once the stream has drained.</returns>
    /// <exception cref="CudaException">The completion signal could not be enqueued.</exception>
    public static ValueTask Arm(IntPtr stream, IntPtr context, CancellationToken cancellationToken) =>
        Rent().ArmCore(stream, context, cancellationToken);

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

    private unsafe ValueTask ArmCore(IntPtr stream, IntPtr context, CancellationToken cancellationToken)
    {
        _stream = stream;
        _context = context;

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

        if (Volatile.Read(ref _pendingVersion) == version)
        {
            _watchdog.Change(s_watchdogPeriod, s_watchdogPeriod);
        }

        return new ValueTask(this, version);
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

        source.TryComplete(source._armedVersion, error: null);
        source.Release();
    }

    private void OnWatchdogTick()
    {
        var version = Volatile.Read(ref _armedVersion);
        if (Volatile.Read(ref _pendingVersion) != version)
        {
            return;
        }

        CUresult status;
        try
        {
            using var scope = new GpuKernelLoader.CudaContextScope(_context);

            // Also observes work enqueued after the completion signal. That is harmless because only fault codes
            // are acted upon; success is always left to the host callback, which owns the reuse handshake.
            status = CudaDriverApi.cuStreamQuery(_stream);
        }
        catch (CudaException ex)
        {
            // The owning context can no longer be made current (e.g. destroyed); surface it instead of hanging.
            TryComplete(version, ex);
            return;
        }

        if (status is CUresult.CudaSuccess or CUresult.CudaErrorNotReady)
        {
            return;
        }

        TryComplete(version, new CudaException(status, $"CUDA stream faulted while awaiting completion: {status}"));
    }

    private void TryComplete(int version, Exception? error)
    {
        if (Interlocked.CompareExchange(ref _pendingVersion, NotArmed, version) != version)
        {
            return;
        }

        _watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (error is null)
        {
            _core.SetResult(true);
        }
        else
        {
            _core.SetException(error);
        }
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
        _stream = IntPtr.Zero;
        _context = IntPtr.Zero;
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
