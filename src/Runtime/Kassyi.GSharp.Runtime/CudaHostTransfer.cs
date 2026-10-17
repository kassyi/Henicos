using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Provides device-to-host transfers that do not block the calling thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> A device-to-host copy into pageable memory (any managed array) is synchronous: it waits for the copy and
/// for all work queued ahead of it on the stream, however long that takes. Transfers are therefore
/// staged through page-locked buffers from a process-wide pool, and completion is signaled by
/// <see cref="CudaStreamCompletion"/> without parking a thread.
/// </para>
/// <para>
/// <b>Chunking:</b> transfers are split into chunks of whole elements of at most 1 MiB, one staging buffer and one
/// completion wait each, so arbitrarily large transfers need only a bounded amount of page-locked memory.
/// </para>
/// <para>
/// <b>Cancellation:</b> only detaches the awaiter; queued device work is not canceled. Once cancellation is observed, no
/// further data is written to the destination, which may already hold the chunks that completed before. A staging
/// buffer returns to the pool only once the device has finished writing to it.
/// </para>
/// <para>
/// <b>Allocation:</b> staging buffers and completion sources are pooled. Page-locked memory is allocated only while the
/// pool grows, and never freed on the caller's path.
/// </para>
/// </remarks>
public static class CudaHostTransfer
{
    /// <summary>
    /// Asynchronously copies <c>destination.Length</c> elements from device memory into <paramref name="destination"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; at most 1 MiB in size.</typeparam>
    /// <param name="devicePointer">
    /// The device address to copy from. In CPU fallback mode, the host address that stands in for device memory.
    /// </param>
    /// <param name="destination">The host memory to fill. Its length determines the number of elements copied.</param>
    /// <param name="stream">
    /// The stream the copy is ordered on, or <see cref="IntPtr.Zero"/> for the legacy null stream.
    /// The copy observes all work enqueued on the stream before this call.
    /// </param>
    /// <param name="cancellationToken">A token that detaches the awaiter; the device work itself is not canceled.</param>
    /// <returns>A <see cref="ValueTask"/> that completes once <paramref name="destination"/> holds the data.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is larger than 1 MiB.</exception>
    /// <exception cref="ArgumentException"><paramref name="devicePointer"/> is <see cref="IntPtr.Zero"/> for a non-empty destination.</exception>
    /// <exception cref="InvalidOperationException">The stream is currently being captured into a CUDA Graph.</exception>
    /// <exception cref="CudaException">The copy could not be enqueued, or the context faulted while waiting.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static ValueTask CopyToHostAsync<T>(IntPtr devicePointer, Memory<T> destination, IntPtr stream,
        CancellationToken cancellationToken = default) where T : unmanaged
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        if (Unsafe.SizeOf<T>() > PinnedStagingBuffer.Size)
        {
            // Rejected in CPU fallback mode too, so that code does not start failing once a device is present.
            throw new NotSupportedException(
                $"Element types larger than the {PinnedStagingBuffer.Size}-byte staging buffer are not supported.");
        }

        if (destination.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        if (devicePointer == IntPtr.Zero)
        {
            throw new ArgumentException("The source address must not be null.", nameof(devicePointer));
        }

        IntPtr context;

        // The scope initializes the driver, which is what populates the CPU fallback flag.
        using (new GpuKernelLoader.CudaContextScope())
        {
            if (GpuKernelLoader.IsCpuFallbackMode)
            {
                CopyFromHostMemory(devicePointer, destination.Span);
                return ValueTask.CompletedTask;
            }

            // Checked before renting or enqueuing: during capture the copy would become a node of the user's graph.
            CudaException.ThrowIfError(CudaDriverApi.cuStreamIsCapturing(stream, out var captureStatus));
            if (captureStatus != CUstreamCaptureStatus.None)
            {
                throw new InvalidOperationException(
                    "Cannot copy on a stream that is being captured into a CUDA Graph. End the capture first.");
            }

            // Resolved once here: continuations run on thread-pool threads, whose current context is unrelated.
            CudaException.ThrowIfError(CudaDriverApi.cuStreamGetCtx(stream, out context));
        }

        return CopyThroughStagingAsync(devicePointer, destination, stream, context, cancellationToken);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask CopyThroughStagingAsync<T>(IntPtr devicePointer, Memory<T> destination,
        IntPtr stream, IntPtr context, CancellationToken cancellationToken) where T : unmanaged
    {
        // Whole elements per chunk: no element straddles two chunks, and byte counts stay within the buffer, so
        // transfers beyond 2 GiB need no 64-bit span arithmetic.
        var elementsPerChunk = PinnedStagingBuffer.Size / Unsafe.SizeOf<T>();
        var staging = PinnedStagingPool.Rent();
        try
        {
            var source = devicePointer;
            var remaining = destination;
            while (!remaining.IsEmpty)
            {
                var count = Math.Min(elementsPerChunk, remaining.Length);
                var byteCount = count * Unsafe.SizeOf<T>();
                await EnqueueChunk(staging, source, byteCount, stream, context, cancellationToken)
                    .ConfigureAwait(false);

                // Only reached when the chunk completed, so a canceled transfer never writes to the destination.
                MemoryMarshal.Cast<byte, T>(staging.GetSpan(byteCount)).CopyTo(remaining.Span[..count]);
                source += byteCount;
                remaining = remaining[count..];
            }
        }
        finally
        {
            staging.Release();
        }
    }

    private static ValueTask EnqueueChunk(PinnedStagingBuffer staging, IntPtr source, int byteCount, IntPtr stream,
        IntPtr context, CancellationToken cancellationToken)
    {
        using var scope = new GpuKernelLoader.CudaContextScope(context);

        // The device holds its own reference until the stream reaches the completion signal behind this copy.
        staging.AddDeviceReference();
        var result = CudaDriverApi.cuMemcpyDtoHAsync_v2(staging.Pointer, source, byteCount, stream);
        if (result != CUresult.CudaSuccess)
        {
            staging.Release(); // Nothing was enqueued, so the device reference is returned right away.
            throw new CudaException(result);
        }

        // If arming throws, the copy is already in flight and the device reference is intentionally never released:
        // the buffer is abandoned rather than reused while the device may still write to it.
        return CudaStreamCompletionSource.Arm(stream, context, cancellationToken, staging);
    }

    private static unsafe void CopyFromHostMemory<T>(IntPtr source, Span<T> destination) where T : unmanaged =>
        new ReadOnlySpan<T>((void*)source, destination.Length).CopyTo(destination);
}
