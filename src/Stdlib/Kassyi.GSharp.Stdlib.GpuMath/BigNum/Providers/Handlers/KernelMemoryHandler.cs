using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Manages unmanaged CUDA device memory allocations, memory clearing, and host-device/device-device asynchronous copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Bridges low-level CUDA Driver API memory operations (<c>cuMemAlloc_v2</c>, <c>cuMemFree_v2</c> / <c>cuMemFreeAsync</c>, <c>cuMemcpyHtoDAsync_v2</c>)
/// into the <see cref="IGpuMemoryProvider"/> contract for GSharp execution pipelines.
/// </para>
/// </remarks>
public sealed class KernelMemoryHandler : IGpuMemoryProvider
{
    private static void InvokeCuda(Func<CUresult> action)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(action());
    }

    private static T InvokeCuda<T>(Func<CUresult> action, Func<T> resultSelector)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(action());
        return resultSelector();
    }

    /// <inheritdoc/>
    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream)
    {
        if (numWords <= 0)
        {
            return;
        }

        if (devicePointer != IntPtr.Zero)
        {
            InvokeCuda(() => CudaDriverApi.cuMemsetD8Async(devicePointer, 0, numWords * 4, stream));
        }
    }

    /// <inheritdoc/>
    public IntPtr AllocateMemory(int numWords)
    {
        var dPtr = IntPtr.Zero;
        return InvokeCuda(() => CudaDriverApi.cuMemAlloc_v2(out dPtr, numWords * 4), () => dPtr);
    }

    /// <inheritdoc/>
    public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0)
    {
        if (devicePointer == IntPtr.Zero)
        {
            return;
        }

        // cuMemFree_v2 waits for every stream of the device; cuMemFreeAsync only orders the release on 'stream'.
        // Allocations from cuMemAlloc_v2 may be released either way.
        InvokeCuda(stream == IntPtr.Zero
            ? () => CudaDriverApi.cuMemFree_v2(devicePointer)
            : () => CudaDriverApi.cuMemFreeAsync(devicePointer, stream));
    }

    /// <inheritdoc/>
    public unsafe void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0)
        {
            return;
        }

        fixed (uint* pHost = hostData)
        {
            var hostPtr = (IntPtr)pHost;
            InvokeCuda(() => CudaDriverApi.cuMemcpyHtoDAsync_v2(devicePointer, hostPtr, Math.Min(hostData.Length, numWords) * 4, stream));
        }
    }

    /// <inheritdoc/>
    public unsafe void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0)
        {
            return;
        }

        fixed (uint* pHost = hostData)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)pHost, devicePointer, numWords * 4, stream));

            // A pageable destination already makes the copy synchronous, but a page-locked one does not. Waiting
            // explicitly keeps the synchronous contract for both, and before 'fixed' unpins the array.
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));
        }
    }

    /// <inheritdoc/>
    public ValueTask CopyDeviceToHostAsync(Memory<uint> hostData, IntPtr devicePointer, int numWords, IntPtr stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numWords, hostData.Length);
        if (numWords <= 0)
        {
            return cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled(cancellationToken)
                : ValueTask.CompletedTask;
        }

        return CudaHostTransfer.CopyToHostAsync(devicePointer, hostData[..numWords], stream, cancellationToken);
    }

    /// <inheritdoc/>
    public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0)
    {
        var pWord = stackalloc uint[1];
        var ptr = (IntPtr)pWord;
        var source = IntPtr.Add(devicePointer, wordOffset * 4);

        if (stream == IntPtr.Zero)
        {
            // Legacy null-stream copy: also waits for all work on every blocking stream of the context.
            return InvokeCuda(() => CudaDriverApi.cuMemcpyDtoH_v2(ptr, source, 4), () => *pWord);
        }

        // Ordered on 'stream' only. The destination is pageable stack memory, so the host must wait for the stream
        // before reading it; that wait covers this stream and does not involve any other stream.
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(ptr, source, 4, stream));
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));
        return *pWord;
    }

    /// <inheritdoc/>
    public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0)
    {
        var destination = IntPtr.Add(devicePointer, wordOffset * 4);

        if (stream == IntPtr.Zero)
        {
            // Legacy null-stream copy: completes before returning and waits for blocking streams.
            var pWord = stackalloc uint[1];
            *pWord = value;
            var ptr = (IntPtr)pWord;
            InvokeCuda(() => CudaDriverApi.cuMemcpyHtoD_v2(destination, ptr, 4));
            return;
        }

        // The value travels in the command itself, so no host buffer has to stay alive and the host does not block.
        InvokeCuda(() => CudaDriverApi.cuMemsetD32Async(destination, value, 1, stream));
    }

    /// <inheritdoc/>
    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0 || destPointer == srcPointer)
        {
            return;
        }

        InvokeCuda(() => CudaDriverApi.cuMemcpyDtoDAsync_v2(destPointer, srcPointer, numWords * 4, stream));
    }

    /// <inheritdoc/>
    public void SynchronizeStream(IntPtr stream)
    {
        if (stream == IntPtr.Zero)
        {
            return;
        }

        InvokeCuda(() => CudaDriverApi.cuStreamSynchronize(stream));
    }

    /// <inheritdoc/>
    public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        // Mirrors SynchronizeStream, which has nothing to wait for on the null stream.
        return stream == IntPtr.Zero
            ? ValueTask.CompletedTask
            : CudaStreamCompletion.WaitAsync(stream, cancellationToken);
    }
}
