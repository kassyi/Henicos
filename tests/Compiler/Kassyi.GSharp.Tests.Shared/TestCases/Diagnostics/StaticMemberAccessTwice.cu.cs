using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// #461: each static field reference is one GSH0065
public static partial class StaticMemberAccessTwiceKernels
{
    private static int s_counter = 3;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void Run(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = s_counter + StaticMemberAccessTwiceKernels.s_counter;
    }
}
