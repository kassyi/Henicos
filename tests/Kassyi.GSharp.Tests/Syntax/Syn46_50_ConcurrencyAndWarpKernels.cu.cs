using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class Syn46_50_ConcurrencyAndWarpKernels
{
    [GpuKernel]
    public static void IntrinsicsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid < 10)
        {
            // Thread 0-9 adds its id to data[0] atomically
            Gpu.AtomicAdd(ref data[0], tid);
        }

        Gpu.SyncThreads();

        if (tid == 0)
        {
            // Just testing math function
            data[1] = System.Math.Max(data[0], 100);
        }
    }

    [GpuKernel]
    public static void AtomicAddStressKernel(int[] globalSum)
    {
        // Every thread in the grid atomically adds 1 to globalSum[0]
        Gpu.AtomicAdd(ref globalSum[0], 1);
    }
}
