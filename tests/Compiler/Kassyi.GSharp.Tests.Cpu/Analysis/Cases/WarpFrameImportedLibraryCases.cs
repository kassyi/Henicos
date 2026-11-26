using Kassyi.GSharp.Core;

namespace ImportedWarpLib;

public static partial class Helpers
{
    [GpuDeviceFunction]
    public static int RemotePairSum(int v) => v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
}
