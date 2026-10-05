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


    /// <inheritdoc/>
    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream)
    {
        if (numWords <= 0)
        {
            return;
        }

        if (devicePointer != IntPtr.Zero)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8Async(devicePointer, 0, numWords * 4, stream));
        }
    }


    /// <inheritdoc/>
    public IntPtr AllocateMemory(int numWords)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var dPtr, numWords * 4));
        return dPtr;
    }


    /// <inheritdoc/>
    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaDriverApi.cuMemFree_v2(devicePointer);
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
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(devicePointer, (IntPtr)pHost,
                Math.Min(hostData.Length, numWords) * 4, stream));
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
        }
    }


    /// <inheritdoc/>
    public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0)
    {
        var pWord = stackalloc uint[1];
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pWord, IntPtr.Add(devicePointer, wordOffset * 4), 4));
        return *pWord;
    }


    /// <inheritdoc/>
    public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value)
    {
        var pWord = stackalloc uint[1];
        *pWord = value;
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyHtoD_v2(IntPtr.Add(devicePointer, wordOffset * 4), (IntPtr)pWord, 4));
    }


    /// <inheritdoc/>
    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0 || destPointer == srcPointer)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoDAsync_v2(destPointer, srcPointer, numWords * 4, stream));
    }

}
