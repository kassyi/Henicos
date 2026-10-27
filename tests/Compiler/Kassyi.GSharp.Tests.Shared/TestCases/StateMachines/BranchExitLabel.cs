using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// The goto after the barrier is in another state than its label: the label starts a state
public static class BranchExitLabel
{
    [GpuKernel]
    public static void BranchExitLabelKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            value += 1;
            goto done;
        }

        value += 10;
    done:
        data[tid] = value * 2;
    }
}
