using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// The mode of Gpu.SyncGrid: a parameter and a local are not constants (GSH0012), a cast of a value that names no member
// is not a mode (GSH0003); the constant spellings are accepted
public static partial class GridSyncModeSadPath
{
    private const GridSyncMode ConstantFast = GridSyncMode.Fast;

    [GpuKernel(Cooperative = true)]
    public static void ModeKernel(int[] data, GridSyncMode mode)
    {
        GridSyncMode local = GridSyncMode.Fast;
        Gpu.SyncGrid(mode);
        Gpu.SyncGrid(local);
        Gpu.SyncGrid((GridSyncMode)5);
        Gpu.SyncGrid();
        Gpu.SyncGrid(GridSyncMode.Deterministic);
        Gpu.SyncGrid(mode: ConstantFast);
        Gpu.SyncGrid((GridSyncMode)1);
        data[0] = 1;
    }
}
