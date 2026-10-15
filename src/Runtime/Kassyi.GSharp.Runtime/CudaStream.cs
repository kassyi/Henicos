
namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Represents a CUDA stream for asynchronous concurrent execution.
/// </summary>
public sealed class CudaStream : IDisposable
{
    /// <summary>
    /// The underlying CUDA stream handle.
    /// </summary>
    public IntPtr Handle
    {
        get;
    }

    /// <summary>
    /// Creates a new non-blocking CUDA stream.
    /// </summary>
    public CudaStream()
    {
        // CU_STREAM_NON_BLOCKING = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out var handle, 1));
        Handle = handle;
    }

    /// <summary>
    /// Blocks the current host thread until all operations in this stream have completed.
    /// </summary>
    public void Synchronize() => CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(Handle));

    /// <summary>
    /// Asynchronously waits until all operations currently enqueued on this stream have completed,
    /// without blocking the calling thread.
    /// </summary>
    /// <param name="cancellationToken">A token that detaches the awaiter; the GPU work itself is not canceled.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the stream has drained.</returns>
    /// <seealso cref="CudaStreamCompletion.WaitAsync"/>
    public ValueTask SynchronizeAsync(CancellationToken cancellationToken = default) =>
        CudaStreamCompletion.WaitAsync(Handle, cancellationToken);

    /// <summary>
    /// Destroys the CUDA stream.
    /// </summary>
    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            // cuStreamDestroy does not implicitly synchronize, but it will complete 
            // all pending work before destroying the stream on the device.
            CudaDriverApi.cuStreamDestroy(Handle);
        }
    }
}

