using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_46_UnsupportedWarpShuffleType
{
    [GpuKernel]
    public static void Kernel(int[] output)
    {
        int tid = ThreadIndex.X;
        string str = "invalid";
        Gpu.WarpShuffle(str, 0);
    }
}
