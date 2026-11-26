using Kassyi.GSharp.Core;

namespace ManagedStackMethodGroupFallbackCases;

[SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
public static partial class MethodGroupFallbackKernels
{
    [GpuDeviceFunction]
    private static void InnerStep(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, ShuffleStep);
    }

    [GpuDeviceFunction]
    private static void ShuffleStep(int i)
    {
        Gpu.SyncThreads();
        uint mask = Warp.ActiveMask();
        _ = Warp.ShuffleXorSync(mask, i, 1);
    }

    [GpuKernel]
    public static void PassesFailedGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, InnerStep);
        data[ThreadIndex.X] += 1;
    }
}
