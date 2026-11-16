using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// Internal so GeneratorSnapshotTests (which compiles this file in a second assembly referencing the test assembly)
// does not see two public extension methods on ReadOnlySpan<int> (#940)
internal static partial class SpanExtensions
{
    [GpuDeviceFunction]
    public static int FirstTwo(this ReadOnlySpan<int> s) => s[0] * 100 + s[1];
}

public static partial class FirstClassSpanKernels
{
    [GpuKernel]
    public static void SpanReceiverKernel(int[] o)
    {
        int t = ThreadIndex.X;
        int[] arr = [t, 7, 3];
        o[t] = arr.FirstTwo();
    }
}
