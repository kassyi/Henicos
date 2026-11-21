using Kassyi.GSharp.Core;

namespace RecursiveStackDiagTest;

public static partial class RecursiveKernels
{
    [GpuDeviceFunction]
    public static void DirectlyRecursiveBarrier(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        Gpu.SyncThreads();
        DirectlyRecursiveBarrier(depth - 1);
    }

    [GpuDeviceFunction]
    public static void NonRecursiveWrapper(int depth)
    {
        DirectlyRecursiveBarrier(depth);
    }

    [GpuKernel]
    public static void DirectRecursionKernel(int depth)
    {
        DirectlyRecursiveBarrier(depth);
    }

    [GpuKernel]
    public static void WrapperRecursionKernel(int depth)
    {
        NonRecursiveWrapper(depth);
    }
}
