using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Represents a contiguous region of device (GPU) memory strongly typed to <typeparamref name="T"/>.
/// Manages allocation and deallocation natively.
/// </summary>
/// <typeparam name="T">The unmanaged type of the elements in the buffer.</typeparam>
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public sealed class GpuBuffer<T> : IDisposable where T : unmanaged
{
    private IntPtr _devicePointer;
    private readonly nint _byteSize;
    private readonly IntPtr _owningContext;
    private readonly bool _isCpuFallback;
    private bool _isDisposed;

    private readonly struct ContextScope : IDisposable
    {
        private readonly bool _pushed;

        public ContextScope(IntPtr owningContext)
        {
            if (owningContext != IntPtr.Zero)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxPushCurrent_v2(owningContext));
                _pushed = true;
            }
            else
            {
                _pushed = false;
            }
        }

        public void Dispose()
        {
            if (_pushed)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _));
            }
        }
    }

    /// <summary>
    /// Gets the raw CUDA device pointer to the allocated memory.
    /// </summary>
    public IntPtr DevicePointer => _devicePointer;

    /// <summary>
    /// Gets the number of elements this buffer can hold.
    /// </summary>
    public int Length
    {
        get;
    }

    /// <summary>
    /// Allocates a new device buffer capable of holding <paramref name="length"/> elements.
    /// </summary>
    /// <param name="length">Number of elements.</param>
    public unsafe GpuBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Length = length;
        var byteSize = checked((nint)length * sizeof(T));
        _byteSize = byteSize;

        if (_byteSize <= 0)
        {
            return;
        }

        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            _isCpuFallback = true;
            _owningContext = IntPtr.Zero;
            _devicePointer = Marshal.AllocHGlobal(_byteSize);
            return;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out _owningContext));
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out _devicePointer, _byteSize));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBuffer{T}"/> class with the specified byte size.
    /// </summary>
    /// <param name="byteSize">The raw byte size of the buffer.</param>
    public GpuBuffer(IntPtr byteSize)
    {
        _byteSize = byteSize;
    }

    /// <summary>
    /// Synchronously copies data from host memory to this device buffer.
    /// </summary>
    /// <param name="source">The host memory span to copy from.</param>
    public unsafe void CopyToDevice(ReadOnlySpan<T> source)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (source.Length > Length)
        {
            throw new ArgumentException("Source span is larger than the allocated device buffer.");
        }

        if (source.Length == 0)
        {
            return;
        }

        var copySize = checked((ulong)(source.Length * sizeof(T)));
        fixed (T* ptr = source)
        {
            if (_isCpuFallback)
            {
                Buffer.MemoryCopy(ptr, (void*)_devicePointer, (ulong)_byteSize, copySize);
                return;
            }

            using var scope = new ContextScope(_owningContext);
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(_devicePointer, (IntPtr)ptr, (nint)copySize));
        }
    }

    /// <summary>
    /// Synchronously copies data from this device buffer to host memory.
    /// </summary>
    /// <param name="destination">The host memory span to copy to.</param>
    public unsafe void CopyToHost(Span<T> destination)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (destination.Length > Length)
        {
            throw new ArgumentException("Destination span is larger than the allocated device buffer.");
        }

        if (destination.Length == 0)
        {
            return;
        }

        var copySize = checked((ulong)(destination.Length * sizeof(T)));
        fixed (T* ptr = destination)
        {
            if (_isCpuFallback)
            {
                Buffer.MemoryCopy((void*)_devicePointer, ptr, copySize, copySize);
                return;
            }

            using var scope = new ContextScope(_owningContext);
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)ptr, _devicePointer, (nint)copySize));
        }
    }

    private CUresult ReleaseUnmanagedResources()
    {
        if (_devicePointer == IntPtr.Zero)
        {
            return CUresult.CudaSuccess;
        }

        if (_isCpuFallback)
        {
            Marshal.FreeHGlobal(_devicePointer);
            _devicePointer = IntPtr.Zero;
            return CUresult.CudaSuccess;
        }

        CUresult res;
        if (_owningContext != IntPtr.Zero)
        {
            CudaDriverApi.cuCtxPushCurrent_v2(_owningContext);
            res = CudaDriverApi.cuMemFree_v2(_devicePointer);
            CudaDriverApi.cuCtxPopCurrent_v2(out _);
        }
        else
        {
            res = CudaDriverApi.cuMemFree_v2(_devicePointer);
        }

        if (res == CUresult.CudaSuccess)
        {
            _devicePointer = IntPtr.Zero;
        }

        return res;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            var res = ReleaseUnmanagedResources();
            if (res == CUresult.CudaSuccess)
            {
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }

            CudaException.ThrowIfError(res);
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="GpuBuffer{T}"/> class.
    /// </summary>
    ~GpuBuffer()
    {
        if (!_isDisposed)
        {
            var res = ReleaseUnmanagedResources();
            if (res == CUresult.CudaSuccess)
            {
                _isDisposed = true;
            }
        }
    }
}
