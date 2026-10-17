
namespace Kassyi.GSharp.Runtime;

/// <summary>
/// A reusable, host-signaled gate that holds CUDA streams on the device via <c>cuStreamWaitValue32_v2</c>
/// without occupying a host thread or stalling the CUDA driver's internal callback thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Gating a stream with a blocking host callback (<c>cuLaunchHostFunc</c>) stalls the CUDA driver's single
/// per-context callback thread, delaying <see cref="CudaStreamCompletion.WaitAsync"/> and all other host functions on
/// every stream of the context. <see cref="CudaStreamGate"/> allocates a 32-bit generation counter in portable,
/// device-mapped page-locked memory and instructs the stream processor to poll that memory location on the device
/// until <see cref="Open"/> performs a volatile host store.
/// </para>
/// <para>
/// <b>Ordering semantics:</b> Only device work enqueued on a stream <i>after</i> <see cref="Enqueue"/> is held until
/// <see cref="Open"/> is called. When overlapping asynchronous host I/O with pre-enqueued GPU work, any host-to-device
/// copy (<c>cuMemcpyHtoDAsync_v2</c>) that consumes the host-filled data must be enqueued <i>after</i>
/// <see cref="Enqueue"/> and must read from page-locked (pinned) host memory so that the DMA engine reads the buffer
/// only after the gate opens.
/// </para>
/// <para>
/// <b>Reuse across generations:</b> <see cref="Close"/> advances a monotonically increasing 32-bit generation counter
/// rather than resetting the flag to zero, and <see cref="Enqueue"/> waits with <c>CU_STREAM_WAIT_VALUE_GEQ</c>
/// (<c>(int)(*flag - generation) &gt;= 0</c>). Streams still draining from an earlier opened generation therefore never
/// race with a subsequent <see cref="Close"/> call.
/// </para>
/// </remarks>
public sealed unsafe class CudaStreamGate : IDisposable
{
    // CU_MEMHOSTALLOC_PORTABLE (0x01) | CU_MEMHOSTALLOC_DEVICEMAP (0x02):
    // Page-locked and mapped into the device address space across all CUDA contexts.
    private const uint HostAllocPortableMappedFlags = 0x01 | 0x02;

    // CU_STREAM_WAIT_VALUE_GEQ (0x0): wait until (int32_t)(*addr - value) >= 0.
    private const uint StreamWaitValueGeqFlag = 0x0;

    private readonly Lock _sync = new();
    private IntPtr _hostPointer;
    private IntPtr _devicePointer;
    private uint _generation;
    private volatile bool _isOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="CudaStreamGate"/> class in the closed state.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The runtime is operating in CPU fallback mode, or the active CUDA device/driver does not support
    /// stream memory operations.
    /// </exception>
    /// <exception cref="CudaException">The driver failed to allocate or map page-locked host memory.</exception>
    public CudaStreamGate()
    {
        using var scope = new GpuKernelLoader.CudaContextScope(GpuKernelLoader.GlobalContext);
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            throw new NotSupportedException(
                "CudaStreamGate requires an active CUDA device and is not supported in CPU fallback mode.");
        }

        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        var attrResult = CudaDriverApi.cuDeviceGetAttribute(
            out var supportsStreamMemOpsV2,
            CudaDriverApi.CuDeviceAttribute.CanUse64BitStreamMemOps,
            device);

        if (attrResult != CUresult.CudaSuccess || supportsStreamMemOpsV2 == 0)
        {
            throw new NotSupportedException(
                "The current CUDA device or driver does not support v2 stream memory operations (cuStreamWaitValue32_v2).");
        }

        CudaException.ThrowIfError(
            CudaDriverApi.cuMemHostAlloc(out _hostPointer, sizeof(uint), HostAllocPortableMappedFlags));
        try
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemHostGetDevicePointer(out _devicePointer, _hostPointer, 0));
        }
        catch
        {
            CudaDriverApi.cuMemFreeHost(_hostPointer);
            _hostPointer = IntPtr.Zero;
            throw;
        }

        Volatile.Write(ref *(uint*)_hostPointer, 0u);
        _generation = 1u;
        _isOpen = false;
    }

    /// <summary>
    /// Gets a value indicating whether the gate is currently open.
    /// </summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Enqueues a device-side wait on <paramref name="stream"/> so that work enqueued on the stream after this call
    /// does not execute until <see cref="Open"/> is called for the current generation.
    /// </summary>
    /// <param name="stream">
    /// The CUDA stream handle to gate, or <see cref="IntPtr.Zero"/> for the legacy default stream.
    /// </param>
    /// <exception cref="ObjectDisposedException">This <see cref="CudaStreamGate"/> has been disposed.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="stream"/> is currently being captured into a CUDA Graph.</exception>
    /// <exception cref="NotSupportedException">The driver reported that <c>cuStreamWaitValue32_v2</c> is not supported.</exception>
    /// <exception cref="CudaException">The driver failed to enqueue the wait operation.</exception>
    public void Enqueue(IntPtr stream)
    {
        IntPtr devicePointer;
        uint generation;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_hostPointer == IntPtr.Zero, this);
            devicePointer = _devicePointer;
            generation = _generation;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamIsCapturing(stream, out var captureStatus));
        if (captureStatus != CUstreamCaptureStatus.None)
        {
            throw new InvalidOperationException(
                "Cannot enqueue a CudaStreamGate on a stream that is being captured into a CUDA Graph. End the capture first.");
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamGetCtx(stream, out var context));
        using var streamScope = new GpuKernelLoader.CudaContextScope(context);

        var result = CudaDriverApi.cuStreamWaitValue32_v2(stream, devicePointer, generation, StreamWaitValueGeqFlag);
        if (result == CUresult.CudaErrorNotSupported)
        {
            throw new NotSupportedException("The CUDA driver does not support cuStreamWaitValue32_v2 on this stream.");
        }

        CudaException.ThrowIfError(result);
    }

    /// <summary>
    /// Opens the gate and releases all streams waiting on the current generation.
    /// </summary>
    /// <remarks>
    /// Performs a single volatile host memory store without invoking any CUDA driver API, so it never blocks and may
    /// be called safely from any thread.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This <see cref="CudaStreamGate"/> has been disposed.</exception>
    public void Open()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_hostPointer == IntPtr.Zero, this);
            Volatile.Write(ref *(uint*)_hostPointer, _generation);
            _isOpen = true;
        }
    }

    /// <summary>
    /// Re-arms an open gate for reuse by advancing the generation counter and transitioning to the closed state.
    /// </summary>
    /// <remarks>
    /// Calling <see cref="Close"/> when the gate is already closed has no effect.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This <see cref="CudaStreamGate"/> has been disposed.</exception>
    public void Close()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_hostPointer == IntPtr.Zero, this);
            if (!_isOpen)
            {
                return;
            }

            _generation++;
            _isOpen = false;
        }
    }

    /// <summary>
    /// Releases any streams still waiting on the gate and frees the underlying page-locked memory.
    /// </summary>
    public void Dispose()
    {
        IntPtr hostPointer;
        lock (_sync)
        {
            if (_hostPointer == IntPtr.Zero)
            {
                return;
            }

            hostPointer = _hostPointer;
            _hostPointer = IntPtr.Zero;
            _devicePointer = IntPtr.Zero;

            // Unblock any streams still waiting on the current generation before calling cuMemFreeHost,
            // which implicitly synchronizes the device and would otherwise deadlock against a closed gate.
            Volatile.Write(ref *(uint*)hostPointer, _generation);
            _isOpen = true;
        }

        using var scope = new GpuKernelLoader.CudaContextScope(GpuKernelLoader.GlobalContext);
        CudaException.ThrowIfError(CudaDriverApi.cuMemFreeHost(hostPointer));
    }
}
