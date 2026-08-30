// ReSharper disable All
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct GpuContext : IDisposable
{
    public int Value;

    public GpuContext(int value)
    {
        Value = value;
    }

    public void Dispose()
    {
        Value = 0;
    }
}

public class Container : IDisposable
{
    private List<GpuContext> _list = new();

    public void Dispose()
    {
        foreach (var c in _list)
        {
            c.Dispose();
        }
    }
}

public static class MEM_03_CollectionsAndGenericsKernels
{
    // 11.4.1 Safe: Generic Constraints (Monomorphization)
    public static void Process<T>(ref T resource) where T : IDisposable
    {
        resource.Dispose(); // OK: Static monomorphized dispatch
    }

    // 11.4.1 Safe: Generic Factory with Ownership
    public static T CreateResource<T>() where T : IDisposable, new()
    {
        return new T(); // OK: Ownership transfers to caller
    }

    // 11.4.2 Safe: Collection Ingestion
    public static void CollectionIngestionDemo()
    {
        var list = new List<GpuContext>();
        list.Add(new GpuContext());
    }

    // 11.4.2 Safe: Implicit foreach Disposal
    public static IEnumerable<GpuContext> GpuQuery()
    {
        yield return new GpuContext();
    }

    public static void ImplicitForeachDemo()
    {
        foreach (var item in GpuQuery())
        {
            /* ... */
        } // OK: Enumerator implicitly disposed by compiler-generated finally block
    }

    // 11.4.3 Safe: Deferred Enumeration (yield)
    public static IEnumerable<GpuContext> Generate()
    {
        yield return new GpuContext();
    }

    [GpuKernel]
    public static void GenericConstraintsKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            GpuContext ctx = new GpuContext(data[tid]);
            ctx.Dispose();
            data[tid] = ctx.Value; // Disposed, value becomes 0
        }
    }

    [GpuKernel]
    public static void GenericFactoryKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            data[tid] = 100;
        }
    }

    [GpuKernel]
    public static void CollectionIngestionKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            var list = new List<int>(64);
            list.Add(data[tid]);
            list.Add(data[tid]);
            data[tid] = list[0] + list[1];
        }
    }

    [GpuKernel]
    public static void ExhaustiveLoopDisposalKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            var list = new List<int>(64);
            list.Add(1);
            list.Add(data[tid]);
            int sum = 0;
            foreach (var x in list)
            {
                sum += x;
                if (x == data[tid]) break;
            }
            data[tid] = sum;
        }
    }

    [GpuKernel]
    public static void ImplicitForeachDisposalKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            var list = new List<int>(64);
            list.Add(5);
            foreach (var item in list)
            {
                data[tid] = data[tid] + item;
            }
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> GenerateData(int start)
    {
        yield return start;
        yield return 10;
    }

    [GpuKernel]
    public static void DeferredEnumerationKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            int sum = 0;
            foreach (var item in GenerateData(data[tid]))
            {
                sum += item;
            }
            data[tid] = sum;
        }
    }
    [GpuDeviceFunction]
    public static IEnumerable<int> GenerateDataNested(int start)
    {
        var list = new List<int>(64);
        list.Add(start);
        list.Add(10);
        list.Add(5);

        foreach (var item in list)
        {
            yield return item;
        }
    }

    [GpuKernel]
    public static void NestedDeferredEnumerationKernel(int[] data, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            int sum = 0;
            foreach (var item in GenerateDataNested(data[tid]))
            {
                sum += item;
            }
            data[tid] = sum;
        }
    }
}
