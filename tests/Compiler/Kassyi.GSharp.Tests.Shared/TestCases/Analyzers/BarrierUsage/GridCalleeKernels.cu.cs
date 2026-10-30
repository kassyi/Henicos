using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A grid barrier reached through a device function: every thread of the grid must reach it, so a branch that varies
// only between blocks diverges for the call as for the direct Gpu.SyncGrid() (#772 second review)
public static partial class GridCalleeKernels
{
    [GpuDeviceFunction]
    public static void GridSyncCallee()
    {
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GridCalleeKernel(int[] buffer)
    {
        if (BlockIndex.X == 0)
        {
            GridSyncCallee();
        }

        Gpu.SyncGrid();
        if (BlockIndex.X == 0)
        {
            Gpu.SyncGrid();
        }

        Gpu.SyncGrid();
    }

    // A block barrier through a device function under the same branch: every thread of a block takes the same side
    [GpuDeviceFunction]
    public static void BlockSyncCallee()
    {
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BlockCalleeKernel(int[] buffer)
    {
        if (BlockIndex.X == 0)
        {
            BlockSyncCallee();
        }

        Gpu.SyncThreads();
    }
}
