using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class DupDemo
{
    [GpuKernel]
    public static void DupDemoKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        {
            int s = tid * 2;
            Gpu.SyncThreads();
            if (s > 4)
            {
                goto done;
            }

            output[tid] = s;
            done: ;
        }
    }
}
