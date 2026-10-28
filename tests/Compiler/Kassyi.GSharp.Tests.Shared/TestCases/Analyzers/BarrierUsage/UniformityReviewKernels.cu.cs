using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public struct ReviewPair
{
    public int X;
    public int Y;
}

// Review of the barrier uniformity analysis: shapes it once missed
public static partial class UniformityReviewKernels
{
    // The exit test of the loop diverges, so the count differs after the loop (cycle exit divergence)
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LoopCountKernel(int[] buffer)
    {
        int n = 0;
        for (int i = 0; i < ThreadIndex.X; i++)
        {
            n++;
        }

        if (n == 0)
        {
            Gpu.SyncThreads(); // LoopCount
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WhileCountKernel(int[] buffer)
    {
        int x = 0;
        while (x < ThreadIndex.X)
        {
            buffer[0] = x;
            x++;
        }

        if (x == 0)
        {
            Gpu.SyncThreads(); // WhileCount
        }

        Gpu.SyncThreads();
    }

    // A field of a struct local assigned a thread-variant value
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StructFieldKernel(int[] buffer)
    {
        ReviewPair p = default;
        p.X = ThreadIndex.X;
        if (p.X % 2 == 0)
        {
            Gpu.SyncThreads(); // StructField
        }

        Gpu.SyncThreads();
    }
}
