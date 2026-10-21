using System.Threading;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Holds a CUDA stream in a pending state on the device via <see cref="CudaStreamGate"/> until a thread-pool timer opens it.
/// </summary>
/// <remarks>
/// The gate opens itself instead of being opened by the test's continuation, so it can never deadlock against a
/// starved thread pool. <see cref="IsReleased"/> therefore tells whether an operation waited for the gated work.
/// </remarks>
internal sealed class StreamGate : IDisposable
{
    private readonly CudaStreamGate _gate;
    private readonly Timer _releaseTimer;

    private StreamGate(CudaStreamGate gate, TimeSpan holdTime)
    {
        _gate = gate;
        _releaseTimer = new Timer(static state =>
        {
            if (state is StreamGate streamGate)
            {
                streamGate.Release();
            }
        }, this, holdTime, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Gets a value indicating whether the gate has opened and the gated stream may proceed.</summary>
    public bool IsReleased => _gate.IsOpen;

    /// <summary>
    /// Enqueues a device-side gate on <paramref name="stream"/> that opens after <paramref name="holdTime"/>.
    /// </summary>
    /// <param name="stream">The stream to hold.</param>
    /// <param name="holdTime">How long the gate stays closed.</param>
    /// <returns>The gate; dispose it to open the gate early and free its resources.</returns>
    public static StreamGate Enqueue(IntPtr stream, TimeSpan holdTime)
    {
        var gate = new CudaStreamGate();
        try
        {
            gate.Enqueue(stream);
        }
        catch
        {
            gate.Dispose();
            throw;
        }

        return new StreamGate(gate, holdTime);
    }

    /// <summary>Opens the gate immediately so the stream is never left blocked, even when an assertion fails early.</summary>
    public void Dispose()
    {
        _releaseTimer.Dispose();
        _gate.Dispose();
    }

    private void Release()
    {
        try
        {
            _gate.Open();
        }
        catch (ObjectDisposedException)
        {
            // Timer fired concurrently with Dispose(), which already opens the gate before freeing memory.
        }
    }
}
