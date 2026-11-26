using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceGotoLoopThroughLoop
{
    // A goto loop whose backward goto sits inside an enclosing for loop: a latch opened after L1 keeps lanes returning
    // from different iterations apart until the outer pass completes
    [GpuKernel]
    public static void GotoLoopThroughForLoopKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int p = -1;
    L1:
        p++;
        result[t + 32 * p] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if (p < 1 && (t & 3) == i + 1)
            {
                goto L1;
            }

            result[t + 32 * (2 + 2 * p + i)] = Warp.ActiveMask();
        }

        result[t + 192] = Warp.ActiveMask();
        result[t + 224] = (uint)p;
    }

    // A goto loop whose gate sits inside a for loop before a break: breaking lanes close the goto loop before jumping
    // to the loop join label, and non-breaking lanes close it after the for loop before the join label
    [GpuKernel]
    public static void GotoLoopLeftInsideForLoopKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
                break;
            }

            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }
}
