using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Once a starts a state, goto b is in a later state than b, so b starts one too
public static class ChainedLabel
{
    [GpuKernel]
    public static void ChainedLabelKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int x = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            x += 100;
            goto a;
        }

        x += 1;
    b:
        x += 2;
    a:
        x += 3;
        if (x < 20)
        {
            goto b;
        }

        data[tid] = x;
    }
}
