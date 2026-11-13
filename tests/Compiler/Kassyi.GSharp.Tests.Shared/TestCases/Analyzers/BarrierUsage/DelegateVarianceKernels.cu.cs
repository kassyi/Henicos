using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A call through a delegate may run any body, so what it returns, or leaves in its out and ref arguments, is
// thread-variant: the analysis does not follow which lambda or local function the delegate holds
public static partial class DelegateVarianceKernels
{
    public delegate void LaneWriter(out int value);

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LambdaConditionKernel(int[] buffer)
    {
        Func<int> lane = () => ThreadIndex.X;
        if (lane() < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LambdaExitKernel(int[] buffer)
    {
        Func<int> lane = () => ThreadIndex.X;
        if (lane() >= 16)
        {
            return;
        }

        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = 1;
    }

    // A local function converted to a delegate, called through Invoke
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LocalFunctionDelegateKernel(int[] buffer)
    {
        static int Lane() => ThreadIndex.X;
        Func<int> lane = Lane;
        if (lane.Invoke() < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void OutArgumentKernel(int[] buffer)
    {
        LaneWriter write = (out int value) => value = ThreadIndex.X;
        write(out int lane);
        if (lane < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = lane;
    }

    [GpuDeviceFunction]
    public static int Apply(Func<int, int> function, int value) => function(value);

    // The delegate is called in a device function, with a uniform argument
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DeviceFunctionKernel(int[] buffer, int n)
    {
        if (Apply(x => x + ThreadIndex.X, n) < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = 1;
    }

    // The control: the same barrier under the lane read directly
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DirectLaneKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = 1;
    }
}
