using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceGotoLoopInIf
{
    // A goto loop whose last backward goto is inside an if without else: the gate closes the loop inside the if
    // branch and is mirrored in a synthesized else branch so both paths hold the same regions after the if
    [GpuKernel]
    public static void GotoLoopInIfElseBranchKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }
}
