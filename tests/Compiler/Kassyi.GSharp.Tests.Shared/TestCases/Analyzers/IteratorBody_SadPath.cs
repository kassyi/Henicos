using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

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
    public static IEnumerable<int> SharedMemoryInIterator(int count)
    {
        SharedMemory<int> shared = new(16);
        shared[0] = count;
        yield return shared[0];
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> UncheckedYieldInIterator(int count)
    {
        unchecked
        {
            yield return count;
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

    [GpuKernel]
    public static void CrossBarrierIteratorLocalKernel(int[] output)
    {
        var seq = Range(0, 4);
        Gpu.SyncThreads();
        foreach (var x in seq)
        {
            output[0] += x;
        }
    }

    [GpuKernel]
    public static void CrossLoopBarrierIteratorLocalKernel(int[] output)
    {
        var seq = Range(0, 4);
        for (int step = 0; step < 2; step++)
        {
            foreach (var x in seq)
            {
                output[0] += x;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ConsumeUncheckedIteratorKernelA(int[] output)
    {
        foreach (var x in UncheckedYieldInIterator(4))
        {
            output[0] += x;
        }
    }

    [GpuKernel]
    public static void ConsumeUncheckedIteratorKernelB(int[] output)
    {
        foreach (var x in UncheckedYieldInIterator(8))
        {
            output[0] += x;
        }
    }
}
