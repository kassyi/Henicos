using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceGotoLoopLatch
{
    // Two backward gotos to L1: a latch opened after L1 reconverges lanes returning by either goto at the start of
    // each pass and is left before the outer loop gate
    [GpuKernel]
    public static void TwoBackwardGotosWithHeaderMaskKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int p = -1;
    L1:
        p++;
        result[t + 32 * p] = Warp.ActiveMask();
        if ((t & 3) == 1 && n++ < 1)
        {
            goto L1;
        }

        result[t + 32 * (2 + p)] = Warp.ActiveMask();
        if ((t & 3) == 2 && n++ < 1)
        {
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }
}
