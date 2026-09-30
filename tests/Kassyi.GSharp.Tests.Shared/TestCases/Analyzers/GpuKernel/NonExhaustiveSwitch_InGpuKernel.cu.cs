using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class SYN_01_NonExhaustiveSwitch_InGpuKernel
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void NonExhaustiveSwitchKernel(int[] val)
    {
        int tid = ThreadIndex.X;
        // Non-exhaustive switch expression (lacks default/discard arm)
        int result = val[tid] switch
        {
            1 => 10,
            2 => 20
        };
    }
}
