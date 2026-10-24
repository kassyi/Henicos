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

[SkipSnapshotConsistency("Analyzer sad path test case")]
public static partial class IteratorBodySadPathKernels
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
    public static IEnumerable<int> BarrierInIterator(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Gpu.SyncThreads();
            yield return i;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> UsingInIterator(int count)
    {
        using var resource = new IteratorDisposable { Value = count };
        yield return resource.Value;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> SharedMemoryInIterator(int count)
    {
        SharedMemory<int> shared = new(16);
        shared[0] = count;
        yield return shared[0];
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

    [GpuDeviceFunction]
    public static IEnumerable<int> UncheckedYieldInIterator(int count)
    {
        unchecked
        {
            yield return count;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> LabeledYieldInIterator(int count)
    {
    L:
        yield return count;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> DeconstructForEachYieldInIterator((int A, int B)[] pairs)
    {
        foreach (var (a, b) in pairs)
        {
            yield return a + b;
        }
    }

    [GpuKernel]
    public static void BarrierInConsumerForEachKernel(int[] output)
    {
        int acc = 0;
        foreach (var x in Range(0, 4))
        {
            Gpu.SyncThreads();
            acc += x;
        }

        output[0] = acc;
    }
}
