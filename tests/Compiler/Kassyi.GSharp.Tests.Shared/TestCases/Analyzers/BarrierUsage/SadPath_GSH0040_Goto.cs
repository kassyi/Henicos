using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SadPath_GSH0040_Goto
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ForwardGotoAcrossBarrierKernel(int[] buffer)
    {
        buffer[0] = 1;
        // GSH0040: goto jumps over a barrier
        goto Skip;

        Gpu.SyncThreads();

    Skip:
        buffer[0] = 2;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void BackwardGotoAcrossBarrierKernel(int[] buffer)
    {
    LoopStart:
        buffer[0]++;
        Gpu.SyncThreads();

        if (buffer[0] < 10)
        {
            // GSH0040: goto jumps backward across a barrier
            goto LoopStart;
        }
    }
}
