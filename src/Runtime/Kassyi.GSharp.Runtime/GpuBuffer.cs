using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Represents a contiguous region of device (GPU) memory strongly typed to <typeparamref name="T"/>.
/// Manages allocation and deallocation natively.
/// </summary>
/// <typeparam name="T">The unmanaged type of the elements in the buffer.</typeparam>
public sealed class GpuBuffer<T> : IDisposable where T : unmanaged
{
    private IntPtr _devicePointer;
    private readonly int _length;
    private readonly int _byteSize;
    private bool _isDisposed;

    /// <summary>
    /// Gets the raw CUDA device pointer to the allocated memory.
    /// </summary>
    public IntPtr DevicePointer => _devicePointer;

    /// <summary>
    /// Gets the number of elements this buffer can hold.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Allocates a new device buffer capable of holding <paramref name="length"/> elements.
    /// </summary>
    /// <param name="length">Number of elements.</param>
    public unsafe GpuBuffer(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _length = length;
        _byteSize = length * sizeof(T);
        
        if (_byteSize > 0)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out _devicePointer, _byteSize));
        }
    }

    /// <summary>
    /// Synchronously copies data from host memory to this device buffer.
    /// </summary>
    /// <param name="source">The host memory span to copy from.</param>
    public unsafe void CopyToDevice(ReadOnlySpan<T> source)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (source.Length > _length)
        {
            throw new ArgumentException("Source span is larger than the allocated device buffer.");
        }

        if (source.Length == 0)
        {
            return;
        }

        fixed (T* ptr = source)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(_devicePointer, (IntPtr)ptr, source.Length * sizeof(T)));
        }
    }

    /// <summary>
    /// Synchronously copies data from this device buffer to host memory.
    /// </summary>
    /// <param name="destination">The host memory span to copy to.</param>
    public unsafe void CopyToHost(Span<T> destination)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (destination.Length > _length)
        {
            throw new ArgumentException("Destination span is larger than the allocated device buffer.");
        }

        if (destination.Length == 0)
        {
            return;
        }

        fixed (T* ptr = destination)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)ptr, _devicePointer, destination.Length * sizeof(T)));
        }
    }

    private void ReleaseUnmanagedResources()
    {
        if (_devicePointer != IntPtr.Zero)
        {
            // Note: In a finalizer context, calling CUDA APIs might be dangerous if the context is already destroyed,
            // but cuMemFree_v2 is generally safe if the driver is still active.
            CudaDriverApi.cuMemFree_v2(_devicePointer);
            _devicePointer = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            ReleaseUnmanagedResources();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    ~GpuBuffer()
    {
        if (!_isDisposed)
        {
            ReleaseUnmanagedResources();
            _isDisposed = true;
        }
    }
}
