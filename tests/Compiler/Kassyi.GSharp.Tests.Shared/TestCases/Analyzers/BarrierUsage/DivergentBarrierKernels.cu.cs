using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class DivergentBarrierKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void IfBranchBarrierKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            // GSH0037: Divergent barrier inside if statement
            Gpu.SyncThreads();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ElseBranchBarrierKernel(int[] buffer)
    {
        if (buffer[ThreadIndex.X] > 0)
        {
            buffer[0] = 1;
        }
        else
        {
            // GSH0037: Divergent barrier inside else block
            Gpu.SyncThreads();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SwitchBranchBarrierKernel(int[] buffer)
    {
        switch (ThreadIndex.X % 2)
        {
            case 0:
                // GSH0037: Divergent barrier inside switch case
                Gpu.SyncThreads();
                break;
            default:
                buffer[0] = 0;
                break;
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }
}
