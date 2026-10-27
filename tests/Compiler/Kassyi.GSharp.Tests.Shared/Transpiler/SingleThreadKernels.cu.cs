using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Kernels whose <c>Gpu.SingleThread</c> bodies must run exactly once per launch on every dialect and launch shape
/// (#667, #690). A body adds 1 to the slot of the thread that runs it; each thread has its own slot, so the sum of the
/// slots is the number of runs and needs no atomics (the managed path has none).
/// </summary>
public static partial class SingleThreadKernels
{
    // The index of the thread in the whole grid, over all three axes
    [GpuDeviceFunction]
    public static int LinearThreadIndex()
    {
        int block = (BlockIndex.Z * GridDimension.Y + BlockIndex.Y) * GridDimension.X + BlockIndex.X;
        int thread = (ThreadIndex.Z * BlockDimension.Y + ThreadIndex.Y) * BlockDimension.X + ThreadIndex.X;
        return block * (BlockDimension.X * BlockDimension.Y * BlockDimension.Z) + thread;
    }

    // The body marks the slot of the thread that runs it
    [GpuKernel]
    public static void MarkKernel(int[] runs)
    {
        int id = LinearThreadIndex();
        Gpu.SingleThread(() =>
        {
            runs[id] += 1;
        });
    }

    // The same after a block barrier, so that the kernel runs as a state machine on the CPU dialects
    [GpuKernel]
    public static void MarkAfterBarrierKernel(int[] runs)
    {
        int id = LinearThreadIndex();
        Gpu.SyncThreads();
        Gpu.SingleThread(() =>
        {
            runs[id] += 1;
        });
    }

    // #667: every thread writes its own element, then the single thread overwrites element 0
    [GpuKernel]
    public static void OverwriteFirstKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        a[tid] = 1;
        Gpu.SingleThread(() =>
        {
            a[0] = 100;
        });
    }

    // A device function that reaches no barrier: the managed path calls it as it is, so Gpu.SingleThread runs the
    // implementation in Kassyi.GSharp.Core
    [GpuDeviceFunction]
    public static void MarkOnce(int[] runs, int id)
    {
        Gpu.SingleThread(() =>
        {
            runs[id] += 1;
        });
    }

    [GpuKernel]
    public static void DeviceFunctionMarkKernel(int[] runs)
    {
        MarkOnce(runs, LinearThreadIndex());
    }
}
