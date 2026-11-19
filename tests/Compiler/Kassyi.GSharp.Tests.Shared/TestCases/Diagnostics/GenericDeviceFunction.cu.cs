using Kassyi.GSharp.Core;

public static partial class GenericDeviceFunctionCases
{
    [GpuDeviceFunction]
    public static T Pick<T>(T a, T b, bool first) where T : unmanaged => first ? a : b;

    [GpuDeviceFunction]
    public static T AfterSync<T>(T value) where T : unmanaged
    {
        T kept = value;
        Gpu.SyncThreads();
        return kept;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void K(int[] xs)
    {
        int tid = ThreadIndex.X;
        xs[tid] = Pick(xs[tid], AfterSync(tid), true) + GenericDeviceFunctionHolder<long>.Twice(tid);
    }
}

// A device function of a generic type that declares no type parameter of its own is emitted once, as before
public partial struct GenericDeviceFunctionHolder<T>
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}
