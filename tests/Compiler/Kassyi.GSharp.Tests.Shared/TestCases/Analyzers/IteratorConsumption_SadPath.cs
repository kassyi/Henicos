using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer sad path test case")]
public static partial class IteratorConsumptionSadPathKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Range(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }

    [GpuDeviceFunction]
    public static void Sink(IEnumerable<int> items)
    {
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> ReturnIterator(int count)
    {
        return Range(0, count);
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> NoYield(int[] a) => a;

    [GpuKernel]
    public static void InvalidConsumptionKernel(int[] output, bool flag)
    {
        Sink(Range(0, 4));

        var selected = flag ? Range(0, 4) : Range(4, 4);
        foreach (var x in selected)
        {
            output[0] += x;
        }

        var seq = Range(0, 4);
        seq = Range(4, 4);
        foreach (var y in seq)
        {
            output[0] += y;
        }

        IEnumerable<int> e1 = output;
        foreach (var z in e1)
        {
            output[0] += z;
        }

        IEnumerable<int> e2 = NoYield(output);
    }
}
