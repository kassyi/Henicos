namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A helper class to measure precise GPU kernel execution time using CUDA Events.
/// </summary>
public sealed class CudaEventTimer : IDisposable
{
    private IntPtr _startEvent;
    private IntPtr _endEvent;
    private bool _disposed;

    public CudaEventTimer()
    {
        // CU_EVENT_DEFAULT = 0x0
        CudaException.ThrowIfError(CudaDriverApi.cuEventCreate(out _startEvent, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuEventCreate(out _endEvent, 0));
    }

    /// <summary>
    /// Records the start event in the specified stream.
    /// </summary>
    public void Start(IntPtr hStream = 0) =>
        CudaException.ThrowIfError(CudaDriverApi.cuEventRecord(_startEvent, hStream));

    /// <summary>
    /// Records the end event in the specified stream.
    /// </summary>
    public void Stop(IntPtr hStream = 0) =>
        CudaException.ThrowIfError(CudaDriverApi.cuEventRecord(_endEvent, hStream));

    /// <summary>
    /// Synchronizes the host thread until the end event has been recorded.
    /// </summary>
    public void Synchronize() => CudaException.ThrowIfError(CudaDriverApi.cuEventSynchronize(_endEvent));

    /// <summary>
    /// Computes the elapsed time between Start and Stop events in milliseconds.
    /// Note: Both events must have been recorded before calling this.
    /// </summary>
    public float GetElapsedTimeMilliseconds()
    {
        CudaException.ThrowIfError(CudaDriverApi.cuEventElapsedTime(out var ms, _startEvent, _endEvent));
        return ms;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_startEvent != IntPtr.Zero)
        {
            CudaDriverApi.cuEventDestroy_v2(_startEvent);
            _startEvent = IntPtr.Zero;
        }

        if (_endEvent != IntPtr.Zero)
        {
            CudaDriverApi.cuEventDestroy_v2(_endEvent);
            _endEvent = IntPtr.Zero;
        }

        _disposed = true;
    }
}
