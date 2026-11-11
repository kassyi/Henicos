using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class SyncGridGapBarrierKernels
{
    // The threads that skip the block barrier wait at the grid synchronization of the next gap while the others wait at
    // the block barrier: a barrier that only some threads reach (GSH0083), not an exit (GSH0084)
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DivergentBarrierBeforeGapKernel(int[] data, int c)
    {
        Gpu.SyncGrid(() =>
        {
            if (ThreadIndex.X < c)
            {
                Gpu.SyncThreads();
            }

            data[ThreadIndex.X] += 1;
        });
    }

    // The divergent barrier is in the last statement of the body: no gap follows, the threads that skip it leave
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DivergentBarrierInLastStatementKernel(int[] data, int c)
    {
        Gpu.SyncGrid(() =>
        {
            data[ThreadIndex.X] += 1;
            if (ThreadIndex.X < c)
            {
                Gpu.SyncThreads();
            }
        });
    }
}
