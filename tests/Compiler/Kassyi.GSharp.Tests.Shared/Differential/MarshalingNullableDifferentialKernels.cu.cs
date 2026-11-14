#nullable enable
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct that lays out differently on CUDA (<c>V</c> at 4 on the host, at 8 on the device).</summary>
public struct NullableDiffInner
{
    public float X;
    public Vector2 V;
}

/// <summary>A struct with an array field, which every launcher converts (#972).</summary>
public struct NullableDiffBag
{
    public int Tag;
    public int[] Data;
}

/// <summary>Nullable fields of every kind in one struct, passed in an array.</summary>
public struct NullableDiffHolder
{
    public float A;
    public Vector2? V;
    public NullableDiffInner? Inner;
    public int? N;
}

/// <summary>Array fields of nullable elements.</summary>
public struct NullableDiffArrayHolder
{
    public Vector2?[] Items;
    public NullableDiffInner?[] Inners;
}

/// <summary>A nullable field whose value holds an array (#972).</summary>
public struct NullableDiffBagHolder
{
    public float A;
    public NullableDiffBag? Bag;
}

/// <summary>
/// Differential kernels for <c>Nullable&lt;T&gt;</c> on every launch path (by value, <c>ref</c> / <c>out</c>, arrays,
/// return values, struct fields, array fields), for a value whose layout differs on CUDA (<c>Vector2?</c>, a user
/// struct), one whose value holds an array and one that is the same everywhere (<c>int?</c>) (#961, #972).
/// </summary>
public static partial class MarshalingNullableDifferentialKernels
{
    public static readonly string[] ScalarColumns =
    [
        "v_has",
        "v_y",
        "inner_has",
        "inner_vy",
        "bag_has",
        "bag_sum",
        "i_val",
    ];

    public static readonly string[] ArrayColumns =
    [
        "a_y",
        "b_vy",
        "c_val",
    ];

    public static readonly string[] FieldColumns =
    [
        "a",
        "v_y",
        "inner_vy",
        "n",
    ];

    public static readonly string[] ArrayFieldColumns =
    [
        "item_y",
        "inner_vy",
    ];

    public static readonly string[] BagColumns =
    [
        "bag_tag",
        "bag_first",
        "holder_has",
        "holder_first",
    ];

    [GpuKernel]
    public static void ScalarsKernel(Vector2? v, NullableDiffInner? inner, NullableDiffBag? bag, int? i, float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= probe.Length / 7)
        {
            return;
        }

        int o = tid * 7;
        probe[o + 0] = v.HasValue ? 1f : 0f;
        probe[o + 1] = v.GetValueOrDefault().Y;
        probe[o + 2] = inner.HasValue ? 1f : 0f;
        probe[o + 3] = inner.GetValueOrDefault().V.Y + inner.GetValueOrDefault().X;
        probe[o + 4] = bag.HasValue ? 1f : 0f;
        float sum = 0f;
        if (bag.HasValue)
        {
            var data = bag.GetValueOrDefault().Data;
            for (int k = 0; k < data.Length; k++)
            {
                sum += data[k];
            }

            sum += bag.GetValueOrDefault().Tag;
        }

        probe[o + 5] = sum;
        probe[o + 6] = i ?? -5;
    }

    [GpuKernel]
    public static void RefsKernel(ref Vector2? v, ref NullableDiffInner? inner, ref int? i, out Vector2? made, int[] o)
    {
        made = null;
        if (BlockIndex.X * BlockDimension.X + ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = v.HasValue ? 1 : 0;
        var old = v.GetValueOrDefault();
        v = v.HasValue ? new Vector2(old.X + 10f, old.Y + 20f) : new Vector2(7f, 8f);
        if (inner.HasValue)
        {
            var value = inner.GetValueOrDefault();
            value.V = new Vector2(value.V.Y, value.X);
            inner = value;
        }

        i = i.HasValue ? null : 42;
        made = new Vector2(1.5f, 2.5f);
    }

    [GpuKernel]
    public static void ArraysKernel(Vector2?[] a, NullableDiffInner?[] b, int?[] c, float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= a.Length)
        {
            return;
        }

        int o = tid * 3;
        probe[o + 0] = a[tid].HasValue ? a[tid].GetValueOrDefault().Y : -1f;
        probe[o + 1] = b[tid].HasValue ? b[tid].GetValueOrDefault().V.Y : -1f;
        probe[o + 2] = c[tid] ?? -1;
        a[tid] = (tid & 1) == 0 ? new Vector2(tid, tid * 2f) : null;
        b[tid] = (tid & 2) == 0 ? null : new NullableDiffInner { X = tid, V = new Vector2(tid + 1f, tid + 2f) };
        c[tid] = (tid & 4) == 0 ? tid * 3 : null;
    }

    [GpuKernel]
    public static Vector2? ReturnVector2Kernel()
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        return (i % 3) == 0 ? null : new Vector2(i + 1f, i + 2f);
    }

    [GpuKernel]
    public static NullableDiffInner? ReturnInnerKernel()
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        return (i % 3) == 1 ? null : new NullableDiffInner { X = i + 1f, V = new Vector2(i + 2f, i + 3f) };
    }

    [GpuKernel]
    public static void FieldsKernel(NullableDiffHolder[] h, float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= h.Length)
        {
            return;
        }

        var item = h[tid];
        int o = tid * 4;
        probe[o + 0] = item.A;
        probe[o + 1] = item.V.HasValue ? item.V.GetValueOrDefault().Y : -1f;
        probe[o + 2] = item.Inner.HasValue ? item.Inner.GetValueOrDefault().V.Y : -1f;
        probe[o + 3] = item.N ?? -1;
        item.V = item.V.HasValue ? null : new Vector2(tid, -tid);
        item.Inner = new NullableDiffInner { X = item.A, V = new Vector2(tid * 0.5f, tid * 0.25f) };
        item.N = item.N.HasValue ? item.N.GetValueOrDefault() + 1 : null;
        h[tid] = item;
    }

    [GpuKernel]
    public static void ArrayFieldsKernel(NullableDiffArrayHolder h, float[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= h.Items.Length)
        {
            return;
        }

        int o = tid * 2;
        probe[o + 0] = h.Items[tid].HasValue ? h.Items[tid].GetValueOrDefault().Y : -1f;
        probe[o + 1] = h.Inners[tid].HasValue ? h.Inners[tid].GetValueOrDefault().V.Y : -1f;
        h.Items[tid] = new Vector2(tid * 3f, tid * 5f);
        h.Inners[tid] = null;
    }

    [GpuKernel]
    public static void BagsKernel(ref NullableDiffBag? bag, NullableDiffBagHolder[] holders, int[] probe)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= holders.Length)
        {
            return;
        }

        int o = tid * 4;
        var value = bag.GetValueOrDefault();
        probe[o + 0] = bag.HasValue ? value.Tag : -1;
        probe[o + 1] = bag.HasValue ? value.Data[tid % value.Data.Length] : -1;
        var holder = holders[tid].Bag;
        probe[o + 2] = holder.HasValue ? 1 : 0;
        probe[o + 3] = holder.HasValue ? holder.GetValueOrDefault().Data[0] : -1;
        if (holder.HasValue)
        {
            holder.GetValueOrDefault().Data[0] = tid + 100;
        }
    }
}
