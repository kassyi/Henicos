using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceLoopLeftAfterContinue
{
    // A while (true) loop left through a break after an if branch that holds its continue: lanes leaving the if
    // wait at the gate before the trailing call until every lane has left the loop, then reconverge at the loop exit
    [GpuKernel]
    public static void LoopLeftAfterContinueKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        while (true)
        {
            result[t + 32 * n] = Warp.ActiveMask();
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    continue;
                }

                result[t + 96] = Warp.ActiveMask();
            }

            break;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }
}
