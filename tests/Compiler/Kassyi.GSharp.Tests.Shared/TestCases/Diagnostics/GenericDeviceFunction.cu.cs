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

// Type parameters that neither the signature nor the body names: compiled once, as one function for every type argument
public static partial class GenericPhantomCases
{
    [GpuDeviceFunction]
    public static void Phantom<T0>(int a, int b, ref int s) => s = a * 3 + b;

    [GpuDeviceFunction]
    public static void PhantomSync<T0, T1>(int a, int b, out int s)
    {
        int kept = a - b;
        Gpu.SyncThreads();
        s = kept * 2;
    }

    [GpuDeviceFunction]
    public static int PhantomForward<T0, T1>(int a, int b)
    {
        int kept = a;
        GenericPhantomCases.Phantom<T0>(a, b, ref kept);
        return kept;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void PhantomKernel(int[] xs)
    {
        int tid = ThreadIndex.X;
        int s = 0;
        GenericPhantomCases.Phantom<byte>(xs[tid], 1, ref s);
        GenericPhantomCases.PhantomSync<int, long>(s, 2, out int u);
        xs[tid] = u + GenericPhantomCases.PhantomForward<short, byte>(u, 3);
    }
}
