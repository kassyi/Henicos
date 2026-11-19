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
        "argument_order_log", "counter", "read_before_add", "meters_sum",
        "mixed_sum",
    ];

    public static readonly string[] SuspendingColumns =
    [
        "plain_sum", "plain_log", "complex_sum", "member_sum", "member_log", "conditional_sum", "shuffle_sum",
        "converted_sum", "assigned_sum", "meters_sum",
    ];

    public static readonly string[] AcrossBarrierColumns =
    [
        "live_sum", "live_log", "nested_sum", "nested_log", "read_before_add",
    ];

    [GpuKernel]
    public static void CollectionInitializerKernel(int[] xs, int[] cells, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0xFF;
        int k = 0;
        int o = tid * 12;

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

        // A user-defined implicit conversion to the parameter type of Add
        r[o + 10] = new CollectionInitializerBag { MetersOf(x) }.Sum;

        // Each element binds an Add of its own: Add(int), then Add(double)
        r[o + 11] = new CollectionInitializerBag { x, 0.5 }.Sum;
    }

    // Each element that is a suspending call or a warp shuffle is the first value C# evaluates in its creation, and
    // the creation initializes or is assigned to a local; the row count is a multiple of the warp size, so every lane
    // reaches the shuffles
    [GpuKernel]
    public static void CollectionInitializerSuspendingKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0xFF;
        int o = tid * 10;

        var plain = new CollectionInitializerBag { ShuffleElement(x), 7 };
        var complex = new CollectionInitializerBag { { ShuffleElement(x), x } };
        var member = new CollectionInitializerHolder { Inner = { ShuffleElement(x), 3 } };
        var conditional = new CollectionInitializerBag { xs.Length > 0 ? ShuffleElement(x) : x };
        var shuffled = new CollectionInitializerBag { Warp.ShuffleXorSync(0xFFFFFFFFu, x, 1) };
        var converted = new CollectionInitializerBag { ShuffleElement(x) * 0.5 };
        CollectionInitializerBag assigned;
        assigned = new CollectionInitializerBag { ShuffleElement(x) };
        var meters = new CollectionInitializerBag { ShuffleMeters(x) };

        r[o + 0] = plain.Sum;
        r[o + 1] = plain.Log;
        r[o + 2] = complex.Sum;
        r[o + 3] = member.Inner.Sum;
        r[o + 4] = member.Inner.Log;
        r[o + 5] = conditional.Sum;
        r[o + 6] = shuffled.Sum;
        r[o + 7] = converted.Sum;
        r[o + 8] = assigned.Sum;
        r[o + 9] = meters.Sum;
    }

    // Collections built before a barrier and read after it
    [GpuKernel]
    public static void CollectionInitializerAcrossBarrierKernel(int[] xs, int[] cells, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0xFF;
        int o = tid * 5;

        var live = new CollectionInitializerBag { x, x + 1 };
        var nested = new CollectionInitializerHolder { Inner = { { x, 2 } } };
        Gpu.SyncThreads();
        r[o + 0] = live.Sum;
        r[o + 1] = live.Log;
        r[o + 2] = nested.Inner.Sum;
        r[o + 3] = nested.Inner.Log;
        r[o + 4] = cells[tid] + new CollectionInitializerBag { { cells, tid } }.Sum;
    }

    [GpuDeviceFunction]
    public static int ShuffleElement(int value) => Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);

    [GpuDeviceFunction]
    public static CollectionInitializerMeters MetersOf(int value) => new CollectionInitializerMeters { Value = value };

    [GpuDeviceFunction]
    public static CollectionInitializerMeters ShuffleMeters(int value)
    {
        int exchanged = ShuffleElement(value);
        return new CollectionInitializerMeters { Value = exchanged };
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

// Converts to int implicitly, as three times its value
public struct CollectionInitializerMeters
{
    public int Value;

    public static implicit operator int(CollectionInitializerMeters meters) => meters.Value * 3;
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

    // An int element binds to Add(int), the better conversion
    [GpuDeviceFunction]
    public static void Add(this ref CollectionInitializerBag bag, double value)
    {
        bag.Sum += (int)(value * 4);
    }

    [GpuDeviceFunction]
    public static void Add(this ref CollectionInitializerBag bag, int left, int right)
    {
        bag.Sum += left * right;
        bag.Log = (bag.Log * 16) + ((left - right) & 15);
    }
}
