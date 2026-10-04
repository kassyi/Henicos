using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A helper class to measure precise GPU kernel execution time using CUDA Events.
/// </summary>
public sealed class CudaEventTimer : IDisposable
{
    private readonly bool _isFallback;
    private IntPtr _startEvent;
    private IntPtr _endEvent;
    private long _startTimestamp;
    private long _endTimestamp;
    private bool _disposed;

#pragma warning disable IDE0051 // Intended for external kernel consumers
    // ReSharper disable UnusedMember.Global

    public CudaEventTimer()
    {
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            _isFallback = true;
            return;
        }

        // CU_EVENT_DEFAULT = 0x0
        CudaException.ThrowIfError(CudaDriverApi.cuEventCreate(out _startEvent, 0));
        try
        {
            CudaException.ThrowIfError(CudaDriverApi.cuEventCreate(out _endEvent, 0));
        }
        catch
        {
            CudaDriverApi.cuEventDestroy_v2(_startEvent);
            _startEvent = IntPtr.Zero;
            throw;
        }
    }

    /// <summary>
    /// Records the start event in the specified stream.
    /// </summary>
    public void Start(IntPtr hStream = 0)
    {
        ThrowIfDisposed();
        if (_isFallback)
        {
            _startTimestamp = Stopwatch.GetTimestamp();
            return;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuEventRecord(_startEvent, hStream));
    }

    /// <summary>
    /// Records the end event in the specified stream.
    /// </summary>
    /// <param name="hStream">The stream to record the event in.</param>
    public void Stop(IntPtr hStream = 0)
    {
        ThrowIfDisposed();
        if (_isFallback)
        {
            _endTimestamp = Stopwatch.GetTimestamp();
            return;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuEventRecord(_endEvent, hStream));
    }

    /// <summary>
    /// Synchronizes the host thread until the end event has been recorded.
    /// </summary>
    public void Synchronize()
    {
        ThrowIfDisposed();
        if (_isFallback)
        {
            return;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuEventSynchronize(_endEvent));
    }

    /// <summary>
    /// Computes the elapsed time between Start and Stop events in milliseconds.
    /// Note: Both events must have been recorded before calling this.
    /// </summary>
    public float GetElapsedTimeMilliseconds()
    {
        ThrowIfDisposed();
        if (_isFallback)
        {
            return (float)((_endTimestamp - _startTimestamp) * 1000.0 / Stopwatch.Frequency);
        }

        CudaException.ThrowIfError(CudaDriverApi.cuEventElapsedTime(out var ms, _startEvent, _endEvent));
        return ms;
    }

    private void ThrowIfDisposed()
    {
        if (!_disposed)
        {
            return;
        }

        throw new ObjectDisposedException(nameof(CudaEventTimer));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_isFallback)
        {
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
        }

        _disposed = true;
    }

    // ReSharper restore UnusedMember.Global
#pragma warning restore IDE0051
}
