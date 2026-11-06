using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// What a local function returns, or leaves in its out and ref arguments, is as thread-variant as what its body puts
// there; the body runs where it is called, with the arguments of that call
public static partial class LocalFunctionVarianceKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReturnedLaneKernel(int[] buffer)
    {
        static int Lane() => ThreadIndex.X;
        int lane = Lane();
        if (lane < 16)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = lane;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void OutLaneKernel(int[] buffer)
    {
        static void Get(out int value)
        {
            value = ThreadIndex.X;
        }

        Get(out int lane);
        if (lane < 16)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = lane;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void RefLaneKernel(int[] buffer)
    {
        static void Add(ref int value)
        {
            value += ThreadIndex.X;
        }

        int lane = 0;
        Add(ref lane);
        if (lane < 16)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = lane;
    }

    // Each return is a constant; which one a thread gets depends on its index
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReturnUnderBranchKernel(int[] buffer)
    {
        static int Pick()
        {
            if (ThreadIndex.X == 0)
            {
                return 1;
            }

            return 2;
        }

        if (Pick() == 1)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = 0;
    }

    // The local function reads a thread-variant local of the kernel
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CapturedLaneKernel(int[] buffer)
    {
        int lane = ThreadIndex.X;
        int Read() => lane;
        if (Read() < 16)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = lane;
    }

    // The same local function, called with a thread-variant and with a uniform argument
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ArgumentKernel(int[] buffer, int n)
    {
        static int Twice(int x) => x * 2;
        if (Twice(ThreadIndex.X) < 16)
        {
            Gpu.SyncThreads();
        }

        if (Twice(n) < 16)
        {
            Gpu.SyncThreads();
        }

        static void Copy(int x, out int y)
        {
            y = x;
        }

        Copy(n, out int same);
        if (same < 16)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = same;
    }

    // The control: the same barrier under the lane read directly
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DirectLaneKernel(int[] buffer)
    {
        int lane = ThreadIndex.X;
        if (lane < 16)
        {
            Gpu.SyncThreads();
        }

        buffer[0] = lane;
    }
}
