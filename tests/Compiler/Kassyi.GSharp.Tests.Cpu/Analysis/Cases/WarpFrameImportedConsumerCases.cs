using Kassyi.GSharp.Core;

namespace ConsumerApp;

public static partial class Kernels
{
    [GpuKernel]
    public static void CallRemoteKernel(int[] data)
    {
        int t = ThreadIndex.X;
        data[t] = ImportedWarpLib.Helpers.RemotePairSum(data[t]);
    }
}
