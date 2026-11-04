#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.PendingBugs;

// #1082: Host-only StackLinq terminal methods ToArray(), CopyTo(Span<T>), and SumSequential() inside a kernel.
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class HostOnlyStackLinqProhibitedKernels
{
    [GpuKernel]
    public static void CallToArrayInKernel(int[] input, int[] output)
    {
        int[] arr = input.AsStackEnumerable().Where(x => x > 0).ToArray();
        output[0] = arr.Length;
    }

    [GpuKernel]
    public static void CallCopyToInKernel(int[] input, int[] output)
    {
        Span<int> buf = stackalloc int[4];
        int copied = input.AsStackEnumerable().Take(4).CopyTo(buf);
        output[0] = copied + buf[0];
    }

    [GpuKernel]
    public static void CallSumSequentialInKernel(float[] input, float[] output)
    {
        output[0] = input.AsStackEnumerable().SumSequential();
    }
}
