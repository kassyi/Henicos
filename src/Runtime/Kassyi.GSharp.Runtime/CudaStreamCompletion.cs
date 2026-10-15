using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Provides non-blocking completion notification for work enqueued on CUDA streams.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> <c>cuStreamSynchronize</c> parks the calling OS thread until the GPU finishes. In high-concurrency
/// hosts (ASP.NET Core, gRPC, background workers) this starves the thread pool. <see cref="WaitAsync"/> releases the
/// calling thread and resumes the continuation on the thread pool once the stream has drained.
/// </para>
/// <para>
/// <b>Allocation:</b> completion sources are pooled, so awaiting with a non-cancelable token does not allocate in steady state.
/// </para>
/// <para>
/// <b>Host-function serialization:</b> the completion signal is a host function (<c>cuLaunchHostFunc</c>). The CUDA driver
/// executes host functions of a context on a single internal thread, so a long-running or blocking host function enqueued
/// by other code on <i>any</i> stream delays completion signals of all streams in that context.
/// </para>
/// </remarks>
public static class CudaStreamCompletion
{
    /// <summary>
    /// Asynchronously waits until all work currently enqueued on <paramref name="stream"/> has completed.
    /// </summary>
    /// <param name="stream">The CUDA stream handle to observe. <see cref="IntPtr.Zero"/> denotes the legacy default stream.</param>
    /// <param name="cancellationToken">
    /// A token that detaches the awaiter. Cancellation never cancels or invalidates the GPU work itself;
    /// the stream keeps executing and remains usable.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the stream has drained.</returns>
    /// <exception cref="InvalidOperationException">The stream is currently being captured into a CUDA Graph.</exception>
    /// <exception cref="CudaException">The completion signal could not be enqueued, or the context faulted while waiting.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// Only work enqueued <i>before</i> this call is observed. In CPU fallback mode the returned task is already completed.
    /// The stream must not be destroyed while the returned task is pending, because fault detection queries it.
    /// </remarks>
    public static ValueTask WaitAsync(IntPtr stream, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            return ValueTask.CompletedTask;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamIsCapturing(stream, out var captureStatus));
        if (captureStatus != CUstreamCaptureStatus.None)
        {
            // A host function enqueued during capture would become a graph node instead of a completion signal.
            throw new InvalidOperationException(
                "Cannot await a stream that is being captured into a CUDA Graph. End the capture first.");
        }

        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out var context));
        return CudaStreamCompletionSource.Arm(stream, context, cancellationToken);
    }
}
