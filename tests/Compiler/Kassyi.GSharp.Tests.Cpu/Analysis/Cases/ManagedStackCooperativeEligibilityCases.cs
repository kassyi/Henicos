using Kassyi.GSharp.Core;

namespace ManagedStackCooperativeEligibilityCases;

[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public static partial class CooperativeEligibilityKernels
{
    [GpuKernel(Cooperative = true)]
    public static void CooperativeSyncGridModeKernel(int[] data)
    {
        data[ThreadIndex.X] = 1;
        Gpu.SyncGrid(GridSyncMode.Fast);
        data[ThreadIndex.X] += 2;
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeSyncGridBodyKernel(int[] data)
    {
        Gpu.SyncGrid(() =>
        {
            data[ThreadIndex.X] = 1;
            data[ThreadIndex.X] += 2;
        });
    }

    [GpuKernel(Cooperative = true)]
    public static void CallsGridBarrierDeviceFunction(int[] data)
    {
        data[ThreadIndex.X] = GridDeviceWait(data[ThreadIndex.X]);
    }

    [GpuDeviceFunction]
    public static int GridDeviceWait(int value)
    {
        Gpu.SyncGrid();
        return value + 1;
    }
}
