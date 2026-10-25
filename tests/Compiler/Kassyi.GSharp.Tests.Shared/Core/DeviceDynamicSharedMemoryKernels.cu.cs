// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that exercise <see cref="SharedMemory{T}"/> declarations inside <c>[GpuDeviceFunction]</c> helpers across
/// multiple calls, caller/callee pairs, size growth, and recursion.
/// </summary>
public static partial class DeviceDynamicSharedMemoryKernels
{
    // D1 (R1): the helper reaches a barrier (lowered on the CPU dialects); its buffer must not overlap the caller's
    [GpuDeviceFunction]
    public static int SuspendingHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        Gpu.SyncThreads();
        return inner[ThreadIndex.X];
    }

    [GpuKernel]
    public static void SuspendingCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = SuspendingHelper(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

    // D8: one helper reached from two kernels; the first declares no buffer of its own, the second has the D1 shape
    [GpuDeviceFunction]
    public static int TwoKernelHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        Gpu.SyncThreads();
        return inner[ThreadIndex.X];
    }

    [GpuKernel]
    public static void HelperOnlyKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        output[tid] = TwoKernelHelper(n, tid);
    }

    [GpuKernel]
    public static void HelperAndCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = TwoKernelHelper(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

    // D2 (R2): the helper never reaches a barrier (not lowered on the CPU dialects)
    [GpuDeviceFunction]
    public static int PlainHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        return inner[ThreadIndex.X];
    }

    [GpuKernel]
    public static void PlainCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = PlainHelper(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

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

    // D4 (R4): the same dynamic declaration reached by two calls of a non-suspending helper
    [GpuDeviceFunction]
    public static int PlainPersistHelper(int n, int val, bool write)
    {
        var s = new SharedMemory<int>(n);
        if (write)
        {
            s[ThreadIndex.X] = val;
        }

        return s[(ThreadIndex.X + 1) % n];
    }

    [GpuKernel]
    public static void PlainPersistKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        PlainPersistHelper(n, tid + 1, true);
        Gpu.SyncThreads();
        output[tid] = PlainPersistHelper(n, 0, false);
    }

    // D5 (R5): static-size shared memory in a non-suspending helper, read back by a second call
    [GpuDeviceFunction]
    public static int StaticPersistHelper(int val, bool write)
    {
        var s = new SharedMemory<int>(32);
        if (write)
        {
            s[ThreadIndex.X] = val;
        }

        return s[(ThreadIndex.X + 1) % 32];
    }

    [GpuKernel]
    public static void StaticPersistKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        StaticPersistHelper(tid + 1, true);
        Gpu.SyncThreads();
        output[tid] = StaticPersistHelper(0, false);
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

    // D7: recursive non-suspending helper (each recursion level reaches the same declaration and must share one buffer)
    [GpuDeviceFunction]
    public static int Rec(int n, int depth)
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(n);
        if (depth == 0)
        {
            s[tid] = tid + 1;
            return s[tid];
        }

        Rec(n, depth - 1);
        s[tid] += 10;
        return s[tid];
    }

    [GpuKernel]
    public static void RecursiveKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        int r = Rec(n, 2);
        output[tid] = outer[tid] + r;
    }
}
