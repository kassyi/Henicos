using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A suspending call whose later argument writes the variable that an earlier identifier argument reads (#608)
public static class SuspendingArgumentOrder
{
    [GpuDeviceFunction]
    public static int Next(ref int counter) => counter++;

    [GpuDeviceFunction]
    public static int CombineAcrossBarrier(int a, int b)
    {
        Gpu.SyncThreads();
        return a * 10 + b;
    }

    [GpuKernel]
    public static void ReadThenWrite(int[] result)
    {
        int a = 0;
        result[0] = CombineAcrossBarrier(a, Next(ref a));
    }

    [GpuKernel]
    public static void ReadThenIndependentWrite(int[] result)
    {
        int a = 0;
        int b = 0;
        result[0] = CombineAcrossBarrier(a, Next(ref b));
    }
}
