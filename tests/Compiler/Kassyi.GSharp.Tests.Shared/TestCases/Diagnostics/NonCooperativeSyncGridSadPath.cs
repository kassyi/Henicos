using Kassyi.GSharp.Core;

public static partial class NonCooperativeSyncGridAuditKernels
{
    [GpuDeviceFunction]
    public static void HelperWithSyncGrid()
    {
        Gpu.SyncGrid();
    }

    [GpuKernel]
    public static void DirectNonCooperativeKernel(int[] data)
    {
        Gpu.SyncGrid();
    }

    // #694: Indirect Gpu.SyncGrid() through a [GpuDeviceFunction] in a non-cooperative kernel
    [GpuKernel]
    public static void IndirectNonCooperativeKernel(int[] data)
    {
        HelperWithSyncGrid();
    }
}
