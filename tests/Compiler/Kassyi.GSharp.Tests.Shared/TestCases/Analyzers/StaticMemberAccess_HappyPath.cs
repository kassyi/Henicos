using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public enum Color
{
    Red,
    Green,
    Blue
}

public static class CfgHappy
{
    public const int N = 4;
    public static readonly int M = 3;
}

public static partial class KHappy
{
    [GpuKernel]
    public static void HappyKernel(int[] a)
    {
        int tid = ThreadIndex.X;
        int bid = BlockIndex.X;
        int bdim = BlockDimension.X;
        int gdim = GridDimension.X;
        int warpSize = Warp.Size;
        Color c = Color.Green;
        int nameLen = nameof(CfgHappy.M).Length;

        a[tid] = CfgHappy.N + (int)c + bid + bdim + gdim + warpSize + nameLen;
    }
}
