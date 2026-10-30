using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// An exhaustive switch expression on the thread index: the discard arm always matches, so the throw of
// SwitchExpressionException that Roslyn keeps after it is never taken and every thread reaches what follows
// (#772 second review)
public static partial class SwitchExpressionBarrierKernels
{
    // The value differs between threads, the barrier after the switch is reached by all
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SwitchExpressionKernel(int[] buffer)
    {
        int v = ThreadIndex.X switch
        {
            0 => 1,
            _ => 2
        };
        if (v == 1)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A switch expression on a uniform value, then a barrier that every thread reaches
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UniformSwitchExpressionKernel(int[] buffer, int count)
    {
        int v = count switch
        {
            0 => 1,
            _ => 2
        };
        buffer[ThreadIndex.X] = v;
        Gpu.SyncThreads();
    }
}
