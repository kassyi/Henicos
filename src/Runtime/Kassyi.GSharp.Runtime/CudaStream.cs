namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Represents a CUDA stream for asynchronous concurrent execution.
/// </summary>
public sealed class CudaStream : IDisposable
{
    private const uint NonBlockingStreamFlag = 1;

    private IntPtr _handle;

    /// <summary>
    /// The underlying CUDA stream handle.
    /// </summary>
    public IntPtr Handle => Volatile.Read(ref _handle);

    /// <summary>
    /// Creates a new non-blocking CUDA stream.
    /// </summary>
    public CudaStream()
    {
        CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out _handle, NonBlockingStreamFlag));
    }

    /// <summary>
    /// Blocks the current host thread until all operations in this stream have completed.
    /// </summary>
    public void Synchronize()
    {
        var handle = Volatile.Read(ref _handle);
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(handle));
    }

    /// <summary>
    /// Asynchronously waits until all operations currently enqueued on this stream have completed,
    /// without blocking the calling thread.
    /// </summary>
    /// <param name="cancellationToken">A token that detaches the awaiter; the GPU work itself is not canceled.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the stream has drained.</returns>
    /// <seealso cref="CudaStreamCompletion.WaitAsync"/>
    public ValueTask SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var handle = Volatile.Read(ref _handle);
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        return CudaStreamCompletion.WaitAsync(handle, cancellationToken);
    }

    /// <summary>
    /// Destroys the CUDA stream.
    /// </summary>
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            // cuStreamDestroy does not implicitly synchronize, but it will complete 
            // all pending work before destroying the stream on the device.
            CudaDriverApi.cuStreamDestroy(handle);
        }
    }
}
