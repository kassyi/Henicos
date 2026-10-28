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

    // A conversion keeps the low bits: every thread takes the branch, so it is no error
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void NarrowingKernel(int[] buffer)
    {
        int tid = ThreadIndex.X;
        if ((byte)(tid * 256) == 0)
        {
            Gpu.SyncThreads(); // Byte
        }

        if (unchecked((int)(tid * 4294967296L)) == 0)
        {
            Gpu.SyncThreads(); // Int
        }

        if ((sbyte)(tid * 128) < 0)
        {
            Gpu.SyncThreads(); // Sbyte
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void SyncIfEven(int value)
    {
        if (value % 2 == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // The argument is the thread index through a local
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void IdentityThroughLocalKernel(int[] buffer)
    {
        int tid = ThreadIndex.X;
        SyncIfEven(tid); // Identity
        Gpu.SyncThreads();
    }

    // A barrier in a body that the CPU dialects do not split
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UnsplitBodyKernel(int[] buffer, int count)
    {
        Gpu.GridStrideLoop(count, i =>
        {
            buffer[i] = i;
            Gpu.SyncThreads(); // Unsplit
        });
    }

    // C# throws for these divisions: no constant, and no exception in the analyzer
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void OverflowingDivisionKernel(int[] buffer)
    {
        if ((long.MinValue + ThreadIndex.X) / -1 > 0)
        {
            Gpu.SyncThreads(); // LongDivide
        }

        if ((long.MinValue + ThreadIndex.X) % -1 == 0)
        {
            Gpu.SyncThreads(); // LongRemainder
        }

        if ((int.MinValue + ThreadIndex.X) / -1 > 0)
        {
            Gpu.SyncThreads(); // IntDivide
        }

        Gpu.SyncThreads();
    }
}
