using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// Values that become thread-variant without a thread-variant operand: a constant returned under a divergent branch of
// the callee, and a lambda parameter written to an outer local (#772 second review)
public static partial class ReturnAndLambdaVarianceKernels
{
    // Each return is a constant; which one a thread gets depends on its index
    [GpuDeviceFunction]
    public static int ReturnUnderBranch()
    {
        if (ThreadIndex.X == 0)
        {
            return 1;
        }

        return 2;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReturnUnderBranchKernel(int[] buffer)
    {
        if (ReturnUnderBranch() == 1)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // The lambda receives a uniform argument here, but a lambda parameter is taken as thread-variant
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LambdaWriteKernel(int[] buffer)
    {
        int flag = 0;
        Func<int, int> f = x =>
        {
            flag = x;
            return x;
        };
        buffer[0] = f(buffer[1]);
        if (flag > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }
}
