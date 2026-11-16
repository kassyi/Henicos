using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A labeled break and continue of a loop that holds a barrier: the loop is flattened into the state machine
public static class LabeledJumpBarrierLoop
{
    [GpuKernel]
    public static void BarrierLabeledKernel(int[] o)
    {
        int t = ThreadIndex.X;
        int s = 0;
        outer:
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                Gpu.SyncThreads();
                if (j == 1 && i == 2) break outer;
                if (j == 2) continue outer;
                s += (i * 4) + j;
            }

            s += 100;
        }

        o[t] = s + t;
    }
}
