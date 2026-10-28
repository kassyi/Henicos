using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// The barrier ends state 0; the label, if it starts a state, starts state 2 after the barrier's state 1
public static class LabelAfterBarrier
{
    [GpuKernel]
    public static void LabelAfterBarrierKernel(int[] data)
    {
        int x = 0;
        Gpu.SyncThreads();
    again:
        x++;
        if (x < 3)
        {
            goto again;
        }

        data[ThreadIndex.X] = x;
    }
}
