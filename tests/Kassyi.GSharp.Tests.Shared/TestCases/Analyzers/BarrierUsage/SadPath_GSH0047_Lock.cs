using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SadPath_GSH0047_Lock
{
    private static readonly object s_gate = new();

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LockStatementKernel(int[] buffer)
    {
        // GSH0047: lock statement is prohibited in GPU kernels
        lock (s_gate)
        {
            buffer[0] = 42;
        }
    }
}
