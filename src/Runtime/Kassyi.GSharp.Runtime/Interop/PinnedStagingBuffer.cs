using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// A page-locked host buffer of <see cref="Size"/> bytes that serves as the DMA target of non-blocking
/// device-to-host copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reference counting:</b> the caller that rented the buffer holds one reference for the whole transfer, and every
/// enqueued copy into the buffer adds one device reference that the stream releases through
/// <see cref="IStreamCompletionHook.OnStreamCompleted"/> once the copy has finished. The buffer returns to
/// <see cref="PinnedStagingPool"/> only when both sides have released it, so a caller that stops waiting (cancellation)
/// can never hand the buffer to another transfer while the device may still write to it.
/// </para>
/// <para>
/// <b>Context:</b> the memory is allocated in <see cref="GpuKernelLoader.GlobalContext"/>, which is never destroyed, with
/// <c>CU_MEMHOSTALLOC_PORTABLE</c>, so copies on streams of isolated contexts stay asynchronous and the buffer outlives
/// every isolated <c>GpuWorkspace</c>.
/// </para>
/// </remarks>
internal sealed unsafe class PinnedStagingBuffer : IStreamCompletionHook, IDisposable
{
    /// <summary>The size of every staging buffer in bytes; larger transfers are split into chunks of this size.</summary>
    public const int Size = 1 << 20;

    // CU_MEMHOSTALLOC_PORTABLE: page-locked for every context, not only for the allocating one.
    private const uint PortableFlag = 0x01;

    private int _references;

    private PinnedStagingBuffer(IntPtr pointer) => Pointer = pointer;

    /// <summary>Gets the host address of the page-locked memory, or <see cref="IntPtr.Zero"/> once freed.</summary>
    public IntPtr Pointer { get; private set; }

    /// <summary>
    /// Allocates a new page-locked buffer. Allocation does not wait for other streams (unlike freeing it).
    /// </summary>
    /// <returns>The new buffer, without any reference held.</returns>
    /// <exception cref="CudaException">The driver could not allocate page-locked memory.</exception>
    public static PinnedStagingBuffer Allocate()
    {
        using var scope = new GpuKernelLoader.CudaContextScope(GpuKernelLoader.GlobalContext);
        CudaException.ThrowIfError(CudaDriverApi.cuMemHostAlloc(out var pointer, Size, PortableFlag));
        return new PinnedStagingBuffer(pointer);
    }

    /// <summary>Gets the first <paramref name="length"/> bytes of the buffer.</summary>
    /// <param name="length">The number of bytes; at most <see cref="Size"/>.</param>
    /// <returns>A read-only view of the staged bytes.</returns>
    public ReadOnlySpan<byte> GetSpan(int length) => new((void*)Pointer, length);

    /// <summary>Takes the caller's reference; called by the pool when the buffer is rented.</summary>
    public void AcquireForCaller() => Volatile.Write(ref _references, 1);

    /// <summary>Adds the reference of a copy that is about to be enqueued into this buffer.</summary>
    public void AddDeviceReference() => Interlocked.Increment(ref _references);

    /// <summary>Releases one reference and returns the buffer to the pool when none remain.</summary>
    /// <remarks>Safe on the driver's host-function thread: returning to the pool performs no CUDA API call.</remarks>
    public void Release()
    {
        var remaining = Interlocked.Decrement(ref _references);
        Debug.Assert(remaining >= 0, "A staging buffer reference was released twice.");
        if (remaining == 0)
        {
            PinnedStagingPool.Return(this);
        }
    }

    /// <inheritdoc/>
    void IStreamCompletionHook.OnStreamCompleted() => Release();

    /// <summary>
    /// Frees the page-locked memory. Called only by the pool for buffers it does not retain.
    /// </summary>
    /// <remarks>
    /// <c>cuMemFreeHost</c> waits for all work on every stream of the device, so this must never run on a caller's thread
    /// or on the driver's host-function thread.
    /// </remarks>
    /// <exception cref="CudaException">The driver failed to free the memory.</exception>
    public void Dispose()
    {
        if (Pointer == IntPtr.Zero)
        {
            return;
        }

        var pointer = Pointer;
        Pointer = IntPtr.Zero;
        using var scope = new GpuKernelLoader.CudaContextScope(GpuKernelLoader.GlobalContext);
        CudaException.ThrowIfError(CudaDriverApi.cuMemFreeHost(pointer));
    }
}
