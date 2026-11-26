using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceGotoLoopInSwitch
{
    // A goto loop whose last backward goto is inside a switch section: the breaking path closes the goto loop at the
    // gate inside case 0, and default closes it before jumping to the switch join label
    [GpuKernel]
    public static void GotoLoopEndingInSwitchSectionKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch ((t & 4) + u)
        {
            case 0:
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }
}
