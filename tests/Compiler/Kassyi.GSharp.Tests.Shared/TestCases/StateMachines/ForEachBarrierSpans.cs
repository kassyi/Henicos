using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// An array foreach whose body holds a barrier, so ForEachRewriter lowers it into a for loop
public static class ForEachBarrierSpans
{
    [GpuKernel]
    public static void ForEachBarrierKernel(int[] values, int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (int value in values)
        {
            output[tid] += value;
            Gpu.SyncThreads();
        }
    }
}
