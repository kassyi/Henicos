using Kassyi.GSharp.Core;

namespace StackTraitsCase;

[SkipSnapshotConsistency("Input for cross-assembly stack traits tests")]
public partial class BaseStackLib
{
    [GpuDeviceFunction]
    public static int WaitBlockPublic(int[] buffer, int tid)
    {
        buffer[tid] = tid;
        Gpu.SyncThreads();
        return buffer[(tid + 1) % 4];
    }

    [GpuDeviceFunction]
    protected static int WaitProtected(int[] buffer, int tid)
    {
        buffer[tid] = tid + 1;
        Gpu.SyncThreads();
        return buffer[(tid + 1) % 4];
    }
}

[SkipSnapshotConsistency("Input for cross-assembly stack traits tests")]
public static partial class StackTraitsLib
{
    [GpuDeviceFunction]
    public static int WaitBlock(int[] buffer, int tid)
    {
        buffer[tid] = tid;
        Gpu.SyncThreads();
        return buffer[(tid + 1) % 4];
    }

    [GpuDeviceFunction]
    public static int WaitShuffle(int value) => Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);

    [GpuDeviceFunction]
    public static void WaitGrid() => Gpu.SyncGrid();

    [GpuDeviceFunction]
    public static uint WaitActiveMask() => Warp.ActiveMask();

    [GpuDeviceFunction]
    public static int PureAdd(int x) => x + 1;

    [GpuDeviceFunction]
    public static T WaitGeneric<T>(T[] buffer, int tid)
    {
        Gpu.SyncThreads();
        return buffer[tid];
    }
}

[SkipSnapshotConsistency("Input for cross-assembly stack traits tests")]
public static class ClosedStackLib
{
    [GpuDeviceFunction]
    public static int WaitClosed(int[] buffer, int tid)
    {
        buffer[tid] = tid;
        Gpu.SyncThreads();
        return buffer[0];
    }
}
