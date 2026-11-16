using System;
using Kassyi.GSharp.Core;

namespace FrameCases;

public static partial class FrameKernels
{
    [GpuKernel]
    public static void SlotsKernel(int[] a, int n)
    {
        int tid = ThreadIndex.X;
        n += tid;
        Span<int> s = stackalloc int[3];
        s[0] = n;
        int kept = s[0] + 1;
        Gpu.SyncThreads();
        a[tid] = kept + n;
    }

    [GpuKernel]
    public static unsafe void PointerKernel(int[] a)
    {
        int tid = ThreadIndex.X;
        int* p = stackalloc int[2];
        p[0] = tid;
        int kept = p[0];
        Gpu.SyncThreads();
        a[tid] = kept;
    }

    [GpuKernel]
    public static void SharedKernel(int[] a)
    {
        int tid = ThreadIndex.X;
        var shared = new SharedMemory<int>(32);
        shared[tid] = a[tid];
        Gpu.SyncThreads();
        a[tid] = shared[31 - tid];
    }

    [GpuKernel]
    public static void CallerKernel(int[] a)
    {
        int tid = ThreadIndex.X;
        int x = Step(a, tid);
        a[tid] = x + Warp.ShuffleDownSync(0xFFFFFFFFu, x, 1u);
    }

    [GpuDeviceFunction]
    private static int Step(int[] a, int tid)
    {
        int before = a[tid];
        Gpu.SyncThreads();
        return before + 1;
    }
}
