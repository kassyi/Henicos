using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SuspendingCallContextKernels
{
    [GpuDeviceFunction]
    public static void Step(int value)
    {
        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void NoBarrier(int value)
    {
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LambdaKernel(int[] buffer)
    {
        Action a = () => Step(0);
        a();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LocalFunctionKernel(int[] buffer)
    {
        void L() => Step(0);
        L();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ForUnrollLambdaKernel(int[] buffer)
    {
        Gpu.ForUnroll(0, 2, i => Step(i));
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SyncGridLambdaKernel(int[] buffer)
    {
        Gpu.SyncGrid(() => Step(0));
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DirectAndCleanKernel(int[] buffer)
    {
        Step(0);
        Action<int> a = NoBarrier;
        a(0);
        buffer[ThreadIndex.X] = nameof(Step).Length;
    }
}
