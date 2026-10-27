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

    // #693: a return in the body leaves the body only, as in C#. The single thread adds min(stop, 4) + 10 to a[0], and
    // every thread, the single one included, still adds 2 to its own element after the bodies. Two bodies with a return
    // in one function need two labels
    [GpuKernel]
    public static void ReturnInBodyKernel(int[] a, int stop)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SingleThread(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == stop)
                {
                    return;
                }

                a[0] += 1;
            }
        });
        Gpu.SingleThread(() =>
        {
            if (stop < 0)
            {
                return;
            }

            a[0] += 10;
        });
        a[tid + 1] += 2;
    }

    // The same after a block barrier: a state machine kernel on the CPU dialects, where the kernel's own return also
    // ends the state machine
    [GpuKernel]
    public static void ReturnInBodyAfterBarrierKernel(int[] a, int stop)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncThreads();
        Gpu.SingleThread(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == stop)
                {
                    return;
                }

                a[0] += 1;
            }
        });
        Gpu.SingleThread(() =>
        {
            if (stop < 0)
            {
                return;
            }

            a[0] += 10;
        });
        a[tid + 1] += 2;
    }

    // In a device function, a return of the body must not end the device function
    [GpuDeviceFunction]
    public static void AddUntil(int[] a, int stop, int tid)
    {
        Gpu.SingleThread(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == stop)
                {
                    return;
                }

                a[0] += 1;
            }
        });
        a[tid + 1] += 2;
    }

    [GpuKernel]
    public static void ReturnInDeviceFunctionBodyKernel(int[] a, int stop)
    {
        AddUntil(a, stop, BlockIndex.X * BlockDimension.X + ThreadIndex.X);
    }

    // The return of a local function of the body leaves the local function, not the body: a[0] = 2 * stop, or 0 when
    // the body returns
    [GpuKernel]
    public static void ReturnInBodyWithLocalFunctionKernel(int[] a, int stop)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SingleThread(() =>
        {
            int Twice(int v)
            {
                return v * 2;
            }

            if (stop == 0)
            {
                return;
            }

            a[0] = Twice(stop);
        });
        a[tid + 1] += 2;
    }

    // A body nested in a body: each return leaves its own body. a[0] = (stop == 1 ? 0 : 1) + (stop == 2 ? 0 : 10)
    [GpuKernel]
    public static void NestedReturnKernel(int[] a, int stop)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SingleThread(() =>
        {
            Gpu.SingleThread(() =>
            {
                if (stop == 1)
                {
                    return;
                }

                a[0] += 1;
            });
            if (stop == 2)
            {
                return;
            }

            a[0] += 10;
        });
        a[tid + 1] += 2;
    }

    // A body with a return in a loop macro: in ForUnroll every unrolled copy gets a label of its own, in GridStrideLoop
    // the label is inside the loop body. Launched with one thread, which runs both iterations: a = [2, 3]
    [GpuKernel]
    public static void ReturnInLoopMacroKernel(int[] a)
    {
        Gpu.ForUnroll(0, 2, k =>
        {
            Gpu.SingleThread(() =>
            {
                if (k == 0)
                {
                    return;
                }

                a[0] += 2;
            });
        });
        Gpu.GridStrideLoop(2, i =>
        {
            Gpu.SingleThread(() =>
            {
                if (i == 0)
                {
                    return;
                }

                a[1] += 3;
            });
        });
    }
}
