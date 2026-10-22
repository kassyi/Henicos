// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that declare several <see cref="SharedMemory{T}"/> buffers, so that a later declaration must not move the
/// buffer an earlier thread already holds.
/// </summary>
public static partial class SharedMemoryRegionKernels
{
    // The first thread declares 'a' before any other thread reaches 'b': 'a' must survive the declaration of 'b'
    [GpuKernel]
    public static void StaleSpanKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(64);
        var b = new SharedMemory<int>(64);
        a[tid] = tid + 1;
        b[tid] = 0;
        Gpu.SyncThreads();
        output[tid] = a[(tid + 1) % 64];
    }

    // Three declarations of different element sizes: every region keeps its own values
    [GpuKernel]
    public static void MixedElementTypesKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var ints = new SharedMemory<int>(64);
        var doubles = new SharedMemory<double>(64);
        var bytes = new SharedMemory<byte>(64);
        ints[tid] = tid + 1;
        doubles[tid] = tid * 0.5;
        bytes[tid] = (byte)(tid + 2);
        Gpu.SyncThreads();
        int next = (tid + 1) % 64;
        output[tid] = ints[next] + (int)(doubles[next] * 2.0) + bytes[next];
    }

    // One dynamic declaration, reached with whatever count the caller passes
    [GpuDeviceFunction]
    public static int RotateThroughDynamicSharedMemory(int value, int count)
    {
        int tid = ThreadIndex.X;
        var shared = new SharedMemory<int>(count);
        if (tid < count)
        {
            shared[tid] = value;
        }

        Gpu.SyncThreads();
        int rotated = tid < count ? shared[(tid + 1) % count] : 0;
        Gpu.SyncThreads();
        return rotated;
    }

    // The second call reaches the same declaration with a larger count, so its region must grow
    [GpuKernel]
    public static void GrowingDeclarationKernel(int[] output, int smallCount, int largeCount)
    {
        int tid = ThreadIndex.X;
        int small = RotateThroughDynamicSharedMemory(tid + 1, smallCount);
        int large = RotateThroughDynamicSharedMemory(tid + 1, largeCount);
        output[tid] = small * 100 + large;
    }
}
