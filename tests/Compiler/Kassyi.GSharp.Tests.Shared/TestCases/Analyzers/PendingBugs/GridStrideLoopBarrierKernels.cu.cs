#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.PendingBugs;

// #490: Direct Gpu.SyncThreads() inside Gpu.GridStrideLoop lambda reports GSH0037 (DivergentBarrierProhibited).
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class GridStrideLoopBarrierKernels
{
    [GpuKernel]
    public static void DirectSyncThreadsInGridStrideLoop(int[] data, int n)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            Gpu.SyncThreads();
            data[i] = i;
        });
    }

    [GpuKernel]
    public static void DirectWarpShuffleInGridStrideLoop(int[] data, int n)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            data[i] = Warp.ShuffleDownSync(0xFFFFFFFFu, i, 1);
        });
    }
}
