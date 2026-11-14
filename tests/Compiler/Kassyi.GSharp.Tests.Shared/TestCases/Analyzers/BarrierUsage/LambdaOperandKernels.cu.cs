using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A lambda passed to a call adds to the call's result what it captures and the thread indices it reads; its own
// parameters count as uniform, since the call decides what it passes
public static partial class LambdaOperandKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LaneInLambdaKernel(int[] data)
    {
        int count = data.AsStackEnumerable().Where(x => x == ThreadIndex.X).Count();
        if (count > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        data[ThreadIndex.X] = count;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CapturedLaneKernel(int[] data)
    {
        int lane = ThreadIndex.X;
        int count = data.AsStackEnumerable().Where(x => x == lane).Count();
        if (count > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        data[ThreadIndex.X] = count;
    }

    [GpuDeviceFunction]
    public static int CountMatches(int[] data, Func<int, bool> predicate) =>
        data.AsStackEnumerable().Where(predicate).Count();

    // The lambda is passed to a device function that runs it
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DeviceFunctionLambdaKernel(int[] data)
    {
        int count = CountMatches(data, x => x == ThreadIndex.X);
        if (count > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        data[ThreadIndex.X] = count;
    }

    // Not reported: the lambda reads only its parameter and a kernel argument
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UniformLambdaKernel(int[] data, int k)
    {
        int count = data.AsStackEnumerable().Where(x => x > k).Select(x => x * 2).Count();
        if (count > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        data[ThreadIndex.X] = count;
    }

    // The control: the lane read directly
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DirectLaneKernel(int[] data)
    {
        if (ThreadIndex.X > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        data[ThreadIndex.X] = 1;
    }
}
