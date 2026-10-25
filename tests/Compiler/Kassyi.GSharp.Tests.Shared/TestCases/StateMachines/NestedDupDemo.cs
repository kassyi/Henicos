using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class NestedDupDemo
{
    [GpuKernel]
    public static void NestedDupDemoKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        {
            int outerLocal = tid + 1;
            {
                int innerLocal = outerLocal * 2;
                Gpu.SyncThreads();
                output[tid] = outerLocal + innerLocal;
            }
        }
    }
}
