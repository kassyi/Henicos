using Kassyi.GSharp.Core;

namespace WarpStructMemberTest;

public struct Guard : System.IDisposable
{
    public int V;

    public int Shuffled => Warp.ShuffleXorSync(0xFFFFFFFFu, V, 1);

    public void Dispose()
    {
        V = Ops.PairSum(V);
    }

    public static implicit operator int(Guard g) => Ops.PairSum(g.V);
}

public static partial class Ops
{
    [GpuDeviceFunction]
    public static int PairSum(int v) => v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);

    [GpuKernel]
    public static void PropertyKernel(int[] data)
    {
        int t = ThreadIndex.X;
        var g = new Guard { V = data[t] };
        Gpu.SyncThreads();
        data[t] = g.Shuffled;
    }

    [GpuKernel]
    public static void ConversionKernel(int[] data)
    {
        int t = ThreadIndex.X;
        var g = new Guard { V = data[t] };
        Gpu.SyncThreads();
        int v = g;
        data[t] = v;
    }

    [GpuKernel]
    public static void UsingKernel(int[] data)
    {
        int t = ThreadIndex.X;
        Gpu.SyncThreads();
        using (var g = new Guard { V = data[t] })
        {
            data[t] = g.V;
        }
    }
}
