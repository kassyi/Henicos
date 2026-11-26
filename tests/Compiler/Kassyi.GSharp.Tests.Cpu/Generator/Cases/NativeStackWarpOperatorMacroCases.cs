using Kassyi.GSharp.Core;

namespace WarpMacroStackTest;

public struct WarpSum
{
    public int V;

    [GpuOperatorMacro(typeof(WarpSumOps), nameof(WarpSumOps.Add))]
    public static WarpSum operator +(WarpSum left, WarpSum right) => throw new GpuOnlyException();
}

public static partial class WarpSumOps
{
    [GpuDeviceFunction]
    public static void Add(ref WarpSum result, WarpSum left, WarpSum right)
    {
        result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
    }
}

public static partial class Kernels
{
    [GpuKernel]
    public static void MacroAfterBarrierKernel(int[] data)
    {
        var x = new WarpSum { V = data[ThreadIndex.X] };
        Gpu.SyncThreads();
        WarpSum z;
        z = x + x;
        data[ThreadIndex.X] = z.V;
    }
}
