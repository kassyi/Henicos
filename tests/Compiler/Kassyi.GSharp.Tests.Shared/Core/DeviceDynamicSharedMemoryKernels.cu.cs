// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that exercise <see cref="SharedMemory{T}"/> declarations inside <c>[GpuDeviceFunction]</c> helpers across
/// multiple calls, caller/callee pairs, size growth, and recursion.
/// </summary>
public static partial class DeviceDynamicSharedMemoryKernels
{
    // D3 (R4s): the same dynamic declaration reached by two calls of a suspending helper (lowered on the CPU dialects)
    [GpuDeviceFunction]
    public static int SuspendingPersistHelper(int n, int val, bool write)
    {
        var s = new SharedMemory<int>(n);
        if (write)
        {
            s[ThreadIndex.X] = val;
        }

        Gpu.SyncThreads();
        return s[(ThreadIndex.X + 1) % n];
    }

    [GpuKernel]
    public static void SuspendingPersistKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        SuspendingPersistHelper(n, tid + 1, true);
        Gpu.SyncThreads();
        output[tid] = SuspendingPersistHelper(n, 0, false);
    }

    // D6 (R6): the helper's declaration is first reached with a small count, then the caller declares its own buffer,
    // then the helper is reached again with a larger count; earlier elements of the grown buffer must also survive
    [GpuDeviceFunction]
    public static int GrowHelper(int count, int writeStart, int val)
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(count);
        if (tid >= writeStart && tid < count)
        {
            s[tid] = val * 2;
        }

        Gpu.SyncThreads();
        int r = tid < count ? s[tid] : 0;
        Gpu.SyncThreads();
        return r;
    }

    [GpuKernel]
    public static void GrowAfterCallerKernel(int[] output, int small, int large)
    {
        int tid = ThreadIndex.X;
        int h1 = GrowHelper(small, 0, tid);
        var outer = new SharedMemory<int>(large);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int h2 = GrowHelper(large, small, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + h2 + h1 * 0;
    }
}
