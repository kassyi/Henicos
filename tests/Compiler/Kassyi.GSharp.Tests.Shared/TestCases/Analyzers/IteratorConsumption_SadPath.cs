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

    [GpuDeviceFunction]
    public static IEnumerable<int> FourItems(int seed)
    {
        yield return seed;
        yield return seed + 1;
        yield return seed + 2;
        yield return seed + 3;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> EightByFour(int seed)
    {
        foreach (var x in FourItems(seed))
        {
            yield return x;
            yield return x + 10;
            yield return x + 20;
            yield return x + 30;
            yield return x + 40;
            yield return x + 50;
            yield return x + 60;
            yield return x + 70;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> SixItems(int seed)
    {
        yield return seed;
        yield return seed + 1;
        yield return seed + 2;
        yield return seed + 3;
        yield return seed + 4;
        yield return seed + 5;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> SixBySix(int seed)
    {
        foreach (var x in SixItems(seed))
        {
            foreach (var y in SixItems(x))
            {
                yield return y;
            }
        }
    }

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

        foreach (var ok in EightByFour(1))
        {
            output[0] += ok;
        }

        foreach (var big in SixBySix(1))
        {
            output[0] += big;
        }
    }
}
