using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// An if / else with a barrier in each branch and a nested if with a barrier in each branch, so BranchRewriter flattens
// all three
public static class NestedBranchBarriers
{
    [GpuKernel]
    public static void NestedBranchKernel(int[] output, int limit)
    {
        int tid = ThreadIndex.X;
        if (tid < limit)
        {
            if (tid == 0)
            {
                output[0] = 1;
                Gpu.SyncThreads();
            }

            output[tid] += 2;
        }
        else
        {
            if (tid == limit)
            {
                output[tid] = 3;
                Gpu.SyncThreads();
            }

            output[tid] += 4;
        }
    }
}
