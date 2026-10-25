using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Each pair declares two locals of the same name in sibling blocks. Only the second 'y' lives across a barrier; both
// 'z' do.
public static class SiblingHoistDemo
{
    [GpuKernel]
    public static void SiblingHoistDemoKernel(int[] a, int[] b)
    {
        int t = ThreadIndex.X;
        {
            int y = 1;
            a[t] = y;
        }

        {
            int y = 2;
            Gpu.SyncThreads();
            b[t] = y;
        }

        {
            int z = 3;
            Gpu.SyncThreads();
            a[t] += z;
        }

        {
            int z = 4;
            Gpu.SyncThreads();
            b[t] += z;
        }
    }
}
