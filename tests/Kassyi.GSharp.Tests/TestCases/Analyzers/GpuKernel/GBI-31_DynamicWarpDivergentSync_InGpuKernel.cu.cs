using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class DynamicWarpDivergentSyncKernel
{
    [GpuKernel]
    public static void Kernel(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid % 2 == 0)
        {
            Gpu.SyncThreads();
        }
        data[tid] = tid;
    }
}
