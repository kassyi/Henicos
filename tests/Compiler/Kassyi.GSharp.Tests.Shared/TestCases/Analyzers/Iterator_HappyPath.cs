using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer happy path test case")]
public static partial class IteratorHappyPathKernels
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
    public static IEnumerable<int> PositiveItems(int[] data)
    {
        foreach (var x in data)
        {
            if (x <= 0)
            {
                yield break;
            }

            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> NestedRange(int count)
    {
        foreach (var outer in Range(0, count))
        {
            foreach (var inner in Range(0, outer))
            {
                yield return outer + inner;
            }
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> LocalSpanWithinSingleStep(int[] data)
    {
        Span<int> first = data;
        var v0 = first[0];
        yield return v0;

        Span<int> second = data;
        var v1 = second[1];
        yield return v1;
    }

    [GpuKernel]
    public static void ConsumeIteratorsKernel(int[] data, int[] output)
    {
        int sum = 0;
        foreach (var x in Range(1, 4))
        {
            sum += x;
        }

        foreach (var y in NestedRange(3))
        {
            sum += y;
        }

        var seq = PositiveItems(data);
        foreach (var a in seq)
        {
            foreach (var b in seq)
            {
                sum += a * b;
            }
        }

        foreach (var z in LocalSpanWithinSingleStep(data))
        {
            sum += z;
        }

        output[0] = sum;
    }
}
