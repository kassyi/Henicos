using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Manages unmanaged CUDA device memory allocations, memory clearing, and host-device/device-device asynchronous copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Bridges low-level CUDA Driver API memory operations (<c>cuMemAlloc_v2</c>, <c>cuMemFree_v2</c>, <c>cuMemcpyHtoDAsync_v2</c>)
/// into the <see cref="IGpuMemoryProvider"/> contract for GSharp execution pipelines.
/// </para>
/// </remarks>
public sealed class AotMemoryHandler : IGpuMemoryProvider
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
    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            InvokeCuda(() => CudaDriverApi.cuMemFree_v2(devicePointer));
        }
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
            var hostPtr = (IntPtr)pHost;
            InvokeCuda(() => CudaDriverApi.cuMemcpyDtoHAsync_v2(hostPtr, devicePointer, numWords * 4, stream));
        }
    }

    /// <inheritdoc/>
    public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0)
    {
        var pWord = stackalloc uint[1];
        var ptr = (IntPtr)pWord;
        return InvokeCuda(() => CudaDriverApi.cuMemcpyDtoH_v2(ptr, IntPtr.Add(devicePointer, wordOffset * 4), 4), () => *pWord);
    }

    /// <inheritdoc/>
    public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value)
    {
        var pWord = stackalloc uint[1];
        *pWord = value;
        var ptr = (IntPtr)pWord;
        InvokeCuda(() => CudaDriverApi.cuMemcpyHtoD_v2(IntPtr.Add(devicePointer, wordOffset * 4), ptr, 4));
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
}
