#nullable enable
using System.Collections;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for collection initializers in GPU code (#1899): C# calls the <c>Add</c> it binds for every
/// element, in the written order, also for an element of several arguments and for the collection a member initializer
/// fills.
/// </summary>
public static partial class CollectionInitializerDifferentialKernels
{
    public static readonly string[] Columns =
    [
        "single_sum", "single_log", "multi_sum", "multi_log", "nested_sum", "nested_log", "order_log",
        "argument_order_log", "counter", "read_before_add",
    ];

    [GpuKernel]
    public static void CollectionInitializerKernel(int[] xs, int[] cells, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0xFF;
        int k = 0;
        int o = tid * 10;

        var single = new CollectionInitializerBag { x, x + 1, 3 };
        var multi = new CollectionInitializerBag { { x, 2 }, { 3, x } };
        var nested = new CollectionInitializerHolder { Inner = { x, 7 } };
        var order = new CollectionInitializerBag { k++ + x, (k++ * 2) + x };
        var argumentOrder = new CollectionInitializerBag { { k++, k++ } };

        r[o + 0] = single.Sum;
        r[o + 1] = single.Log;
        r[o + 2] = multi.Sum;
        r[o + 3] = multi.Log;
        r[o + 4] = nested.Inner.Sum;
        r[o + 5] = nested.Inner.Log;
        r[o + 6] = order.Log;
        r[o + 7] = argumentOrder.Log;
        r[o + 8] = k;

        // C# reads cells[tid] before the Add of the element writes it
        r[o + 9] = cells[tid] + new CollectionInitializerBag { { cells, tid } }.Sum;
    }
}

public struct CollectionInitializerBag : IEnumerable
{
    public int Sum;
    public int Log;

    public IEnumerator GetEnumerator() => throw new System.NotSupportedException();
}

public struct CollectionInitializerHolder
{
    public CollectionInitializerBag Inner;
}

public static partial class CollectionInitializerBagExtensions
{
    // Each call appends the low four bits of its value to Log, so Log records the order of the calls
    [GpuDeviceFunction]
    public static void Add(this ref CollectionInitializerBag bag, int value)
    {
        bag.Sum += value;
        bag.Log = (bag.Log * 16) + (value & 15);
    }

    // Adds 100 to cells[index], then the new value of the cell to Sum
    [GpuDeviceFunction]
    public static void Add(this ref CollectionInitializerBag bag, int[] cells, int index)
    {
        cells[index] += 100;
        bag.Sum += cells[index];
    }

    [GpuDeviceFunction]
    public static void Add(this ref CollectionInitializerBag bag, int left, int right)
    {
        bag.Sum += left * right;
        bag.Log = (bag.Log * 16) + ((left - right) & 15);
    }
}
