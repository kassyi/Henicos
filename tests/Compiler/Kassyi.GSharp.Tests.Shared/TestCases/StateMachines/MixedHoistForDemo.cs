using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Without a semantic model the hoister keys declarations by name, so the 'a' of the for statement moves into the context
// with the 'a' of the block, which lives across a barrier; 'b' stays a local and reads 'a'.
public static class MixedHoistForDemo
{
    [GpuKernel]
    public static void MixedHoistForDemoKernel(int[] o)
    {
        int t = ThreadIndex.X;
        {
            int a = t;
            Gpu.SyncThreads();
            o[t] = a;
        }

        for (int a = 1, b = a + 1; b < 4; b++)
        {
            o[t] += b;
        }
    }
}
