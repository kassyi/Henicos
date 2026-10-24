using System;
using System.Collections;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer sad path test case")]
public partial class IteratorShapeSadPathKernels
{
    [GpuKernel]
    public static void KernelWithLocalFunctionIterator(int[] output)
    {
        IEnumerable<int> LocalIterator(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return i;
            }
        }

        output[0] = 0;
    }

    [GpuDeviceFunction]
    public static IEnumerator<int> EnumeratorReturn(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable NonGenericEnumerableReturn(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    [GpuDeviceFunction]
    public IEnumerable<int> InstanceIterator(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<Span<int>> SpanElementIterator(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return default;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<T> GenericIter<T>(T x) where T : unmanaged
    {
        yield return x;
    }
}
