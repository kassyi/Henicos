using Kassyi.GSharp.Core;

public static partial class WarpIdentityBarrierKernels
{
    // Gpu.WarpId() and Gpu.LaneId() are plain intrinsics: they must not split the state machine like a warp shuffle
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WarpIdentityAcrossBarrierKernel(int[] warpIds, int[] laneIds)
    {
        int tid = ThreadIndex.X;
        int warp = Gpu.WarpId();
        int lane = Gpu.LaneId();

        Gpu.SyncThreads();

        warpIds[tid] = warp + Gpu.WarpId();
        laneIds[tid] = lane + Gpu.LaneId();
    }
}
