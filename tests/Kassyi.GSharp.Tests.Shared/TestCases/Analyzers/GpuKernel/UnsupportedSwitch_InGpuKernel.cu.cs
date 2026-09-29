using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class SYN_01_UnsupportedSwitch_InGpuKernel
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void UnsupportedSwitchKernel(int[] val)
    {
        int tid = ThreadIndex.X;
        
        // Unsupported: when clause
        int result1 = val[tid] switch
        {
            1 when tid == 0 => 10,
            _ => 20
        };
        
        // Unsupported: non-constant pattern
        int result2 = val[tid] switch
        {
            > 0 => 10,
            _ => 20
        };
    }
}
