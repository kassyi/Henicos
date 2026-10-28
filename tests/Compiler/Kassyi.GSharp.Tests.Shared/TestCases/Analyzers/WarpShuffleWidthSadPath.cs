using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Constant shuffle widths that CUDA leaves undefined (not 1, 2, 4, 8, 16 or 32), and widths that are fine or known only
// at run time
public class WarpShuffleWidthSadPath
{
    [GpuKernel]
    public static void WidthKernel(int[] r, int w)
    {
        const int Twelve = 12;
        int v = r[ThreadIndex.X];
        v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1u, 3);
        v = Warp.ShuffleSync(0xFFFFFFFFu, v, 0, 64);
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1, 0);
        v = Warp.ShuffleUpSync(0xFFFFFFFFu, v, 1u, -8);
        v = Warp.ShuffleSync(0xFFFFFFFFu, v, 0, width: Twelve);
        v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1u, 16);
        v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1u, w);
        v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1u);
        v = Warp.ShuffleUpSync(0xFFFFFFFFu, v, 1u, 1);
        r[ThreadIndex.X] = v;
    }
}
