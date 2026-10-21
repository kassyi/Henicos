using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class DivergentBarrierKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void IfBranchBarrierKernel(Dim3 threadIdx, int[] buffer)
    {
        if (threadIdx.X < 16)
        {
            // GSH0037: Divergent barrier inside if statement
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ElseBranchBarrierKernel(int condition, int[] buffer)
    {
        if (condition > 0)
        {
            buffer[0] = 1;
        }
        else
        {
            // GSH0037: Divergent barrier inside else block
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SwitchBranchBarrierKernel(int mode, int[] buffer)
    {
        switch (mode)
        {
            case 0:
                // GSH0037: Divergent barrier inside switch case
                Gpu.SyncThreads();
                break;
            default:
                buffer[0] = 0;
                break;
        }
    }
}
