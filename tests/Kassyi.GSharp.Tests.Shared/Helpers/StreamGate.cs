using System.Runtime.InteropServices;
using System.Threading;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Holds a CUDA stream in a pending state by enqueuing a host function that blocks until a thread-pool timer releases it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Test-only:</b> the driver runs host functions of a context on a single thread, so a closed gate also delays every
/// other host function and device-wide synchronizing call of the context. Tests using it must run in
/// <see cref="TestCollections.DeviceExclusive"/>. Production code should gate streams on the device instead (see #321).
/// </para>
/// <para>
/// The gate opens itself instead of being opened by the test's continuation, so it can never deadlock against a
/// starved thread pool. <see cref="IsReleased"/> therefore tells whether an operation waited for the gated work.
/// </para>
/// </remarks>
internal sealed unsafe class StreamGate : IDisposable
{
    // ManualResetEventSlim is never disposed: the driver thread may still be waiting on it, and without an exposed
    // WaitHandle it owns no kernel object.
    private readonly ManualResetEventSlim _released = new(false);
    private readonly Timer _releaseTimer;
    private GCHandle _handle;

    private StreamGate(TimeSpan holdTime)
    {
        _handle = GCHandle.Alloc(this);
        _releaseTimer = new Timer(static state =>
        {
            if (state is StreamGate gate)
            {
                gate._released.Set();
            }
        }, this, holdTime, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Gets a value indicating whether the gate has opened and the gated stream may proceed.</summary>
    public bool IsReleased => _released.IsSet;

    /// <summary>
    /// Enqueues a gate on <paramref name="stream"/> that opens after <paramref name="holdTime"/>.
    /// </summary>
    /// <param name="stream">The stream to hold. The stream's context must be current.</param>
    /// <param name="holdTime">How long the gate stays closed.</param>
    /// <returns>The gate; dispose it to open the gate early.</returns>
    public static StreamGate Enqueue(IntPtr stream, TimeSpan holdTime)
    {
        var gate = new StreamGate(holdTime);
        delegate* unmanaged<IntPtr, void> callback = &OnStreamReached;
        var result = CudaDriverApi.cuLaunchHostFunc(stream, (IntPtr)callback, GCHandle.ToIntPtr(gate._handle));
        if (result != CUresult.CudaSuccess)
        {
            // The driver never took the handle, so nothing else will free it.
            gate._releaseTimer.Dispose();
            gate._handle.Free();
            throw new CudaException(result);
        }

        return gate;
    }

    /// <summary>Opens the gate immediately so the driver thread is never left blocked, even when an assertion fails early.</summary>
    public void Dispose()
    {
        _released.Set();
        _releaseTimer.Dispose();
    }

    [UnmanagedCallersOnly]
    private static void OnStreamReached(IntPtr userData)
    {
        if (GCHandle.FromIntPtr(userData).Target is not StreamGate gate)
        {
            return;
        }

        gate._released.Wait();
        gate._handle.Free();
    }
}
