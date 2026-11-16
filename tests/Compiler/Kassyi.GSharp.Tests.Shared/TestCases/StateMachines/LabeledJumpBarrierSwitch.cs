using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A labeled break and continue from the section of a switch that holds a barrier, to the loop around the switch
public static class LabeledJumpBarrierSwitch
{
    [GpuKernel]
    public static void BarrierSwitchLabeledKernel(int[] o)
    {
        int t = ThreadIndex.X;
        int s = 0;
        outer:
        for (int i = 0; i < 4; i++)
        {
            switch (i & 1)
            {
                case 0:
                    Gpu.SyncThreads();
                    if (i == 2) break outer;
                    s += 1;
                    break;
                default:
                    for (int j = 0; j < 2; j++)
                    {
                        Gpu.SyncThreads();
                        if (j == 1) continue outer;
                    }

                    break;
            }

            s += 10;
        }

        o[t] = s + t;
    }
}
