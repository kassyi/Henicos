using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceGotoLoopInBlock
{
    // The goto loop closes right after the if with its last goto back to top, inside the inner block
    [GpuKernel]
    public static void GotoLoopInNestedBlockKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        {
            {
                n++;
                if ((t & 1) == 1 && n < 2)
                {
                    goto top;
                }

                r[t + 32] = Warp.ActiveMask();
            }

            r[t + 64] = Warp.ActiveMask();
        }

        r[t + 96] = Warp.ActiveMask();
    }
}
