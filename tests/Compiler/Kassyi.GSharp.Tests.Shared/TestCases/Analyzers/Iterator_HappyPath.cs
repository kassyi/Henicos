using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct IteratorDisposable : IDisposable
{
    public int Value;

    public void Dispose()
    {
    }
}

[InlineArray(4)]
public struct IntBuffer4
{
    private int _element0;
}

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

    [GpuDeviceFunction]
    public static IEnumerable<int> UsingInIterator(int count)
    {
        using var resource = new IteratorDisposable { Value = count };
        yield return resource.Value;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> SwitchYieldInIterator(int mode)
    {
        switch (mode)
        {
            case 0:
                yield return 10;
                break;
            default:
                yield return 20;
                break;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> InlineArrayForEachInIterator(IntBuffer4 buffer)
    {
        foreach (var item in buffer)
        {
            yield return item;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> CrossYieldSpanInIterator(int[] data)
    {
        Span<int> slice = data;
        yield return slice[0];
        yield return slice[1];
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

        foreach (var u in UsingInIterator(5))
        {
            sum += u;
        }

        foreach (var s in SwitchYieldInIterator(data[0]))
        {
            sum += s;
        }

        IntBuffer4 buffer = default;
        buffer[0] = 1;
        buffer[1] = 2;
        buffer[2] = 3;
        buffer[3] = 4;
        foreach (var item in InlineArrayForEachInIterator(buffer))
        {
            sum += item;
        }

        foreach (var c in CrossYieldSpanInIterator(data))
        {
            sum += c;
        }

        output[0] = sum;
    }
}
