using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// A method named nameof is an ordinary call (legal C#): a method group in its argument is checked like any other
public static partial class UserNameofMethodGroupKernels
{
    [GpuDeviceFunction]
    private static void nameof(Action<int[]> step) { }

    private static void HostStep(int[] data) => data[0] = 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CalleeKernel(int[] data)
    {
        data[ThreadIndex.X] += 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void PassesGroupsToUserNameofKernel(int[] data)
    {
        nameof(CalleeKernel);
        nameof(HostStep);
        data[ThreadIndex.X] += 1;
    }
}

// The nameof operator reads no method: no diagnostic
public static partial class NameofOperatorMethodGroupKernels
{
    private static void HostStep(int[] data) => data[0] = 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NamesHostStepKernel(int[] data)
    {
        data[ThreadIndex.X] = nameof(HostStep).Length;
    }
}
