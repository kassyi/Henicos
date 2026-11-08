using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// One kernel per macro that PreLoweringExpander expands: a ForUnroll and a GridStrideLoop whose body holds a barrier,
// and a SyncGrid body that returns early, so that its later statements are guarded and its declaration is split
public static class PreLoweringMacroSpans
{
    [GpuKernel]
    public static void ForUnrollBarrierKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        Gpu.ForUnroll(0, 4, k =>
        {
            output[tid] += k;
            Gpu.SyncThreads();
        });
    }

    [GpuKernel]
    public static void GridStrideBarrierKernel(int[] output, int count)
    {
        int tid = ThreadIndex.X;
        Gpu.GridStrideLoop(count, i =>
        {
            output[i] += tid;
            Gpu.SyncThreads();
        });
    }

    [GpuKernel]
    public static void SyncGridBodyKernel(int[] output, int limit)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            if (tid >= limit)
            {
                return;
            }

            int value = output[tid];
            output[tid] = value + 1;
        });
    }
}
