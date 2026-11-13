#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public enum DefaultByteEnum : byte
{
    Zero = 0,
    One = 1
}

/// <summary>
/// Differential kernels for <c>default(T)</c> and the <c>default</c> literal in positions where C++ has no braced list:
/// <c>var</c>, a conditional arm, a comparison, an operand, a return value, a struct, a nullable, an array and a
/// pointer. Each kernel checks one shape; the types cover every spelling of a C++ type (one word, several words).
/// </summary>
public static unsafe partial class DefaultValueDifferentialKernels
{
    [GpuKernel]
    public static void DefaultVarKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        var z = default(int);
        var w = default(long);
        var u = default(uint);
        var ul = default(ulong);

        int col = t * 4;
        dst[col + 0] = z + a[t];
        dst[col + 1] = (int)(w + a[t]);
        dst[col + 2] = (int)(u + (uint)a[t]);
        dst[col + 3] = (int)((ul + (ulong)a[t]) & 0xFF);
    }

    [GpuKernel]
    public static void DefaultConditionalArmKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int y = a[t] > 0 ? a[t] : default;
        uint u = a[t] > 0 ? default : 5u;
        long l = a[t] > 0 ? default : 7L;

        int col = t * 3;
        dst[col + 0] = y;
        dst[col + 1] = (int)u;
        dst[col + 2] = (int)l;
    }

    [GpuKernel]
    public static void DefaultNarrowTypesKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        var b = default(byte);
        var sb = default(sbyte);
        var s = default(short);
        var us = default(ushort);
        byte picked = a[t] > 0 ? default : (byte)9;
        DefaultByteEnum e = a[t] > 0 ? default : DefaultByteEnum.One;
        var ev = default(DefaultByteEnum);

        int col = t * 7;
        dst[col + 0] = b + 1;
        dst[col + 1] = sb + 2;
        dst[col + 2] = s + 3;
        dst[col + 3] = us + 4;
        dst[col + 4] = picked;
        dst[col + 5] = (int)e;
        dst[col + 6] = (int)ev;
    }

    [GpuKernel]
    public static void DefaultComparisonAndOperandKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        bool equal = a[t] == default(int);
        bool notEqual = a[t] != default;
        long sum = default(long) + a[t];

        int col = t * 3;
        dst[col + 0] = equal ? 1 : 0;
        dst[col + 1] = notEqual ? 1 : 0;
        dst[col + 2] = (int)sum;
    }

    [GpuDeviceFunction]
    public static long ReturnDefaultLong(int c, long x)
    {
        if (c > 0)
        {
            return default;
        }

        return x;
    }

    [GpuDeviceFunction]
    public static uint ReturnDefaultUInt(int c, uint x) => c > 0 ? default : x;

    [GpuKernel]
    public static void DefaultReturnKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int col = t * 2;
        dst[col + 0] = (int)ReturnDefaultLong(a[t], 11L);
        dst[col + 1] = (int)ReturnDefaultUInt(a[t], 13u);
    }

    [GpuKernel]
    public static void DefaultStructKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        DiffNullablePairHolder holder = a[t] > 0 ? default : new DiffNullablePairHolder { Tag = 7 };
        int tag = default(DiffNullablePairHolder).Tag + 1;

        int col = t * 3;
        dst[col + 0] = holder.Tag;
        dst[col + 1] = holder.First.HasValue ? 1 : 0;
        dst[col + 2] = tag;
    }

    [GpuKernel]
    public static void DefaultNullableKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int? maybe = a[t] > 0 ? default(int?) : a[t];
        int? q = a[t] > 0 ? default(int) : null;
        bool isDefault = maybe == default;
        bool isNotDefault = maybe != default;
        bool defaultOnLeft = default == maybe;

        int col = t * 5;
        dst[col + 0] = maybe.HasValue ? 1 : 0;
        dst[col + 1] = q.HasValue ? 1 : 0;
        dst[col + 2] = isDefault ? 1 : 0;
        dst[col + 3] = isNotDefault ? 1 : 0;
        dst[col + 4] = defaultOnLeft ? 1 : 0;
    }

    [GpuKernel]
    public static void DefaultArrayKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int[] none = a[t] > 0 ? default! : a;
        bool isDefault = none == default;
        bool otherIsDefault = a == default;

        int col = t * 3;
        dst[col + 0] = none == null ? 1 : 0;
        dst[col + 1] = isDefault ? 1 : 0;
        dst[col + 2] = otherIsDefault ? 1 : 0;
    }

    [GpuKernel]
    public static void DefaultPointerKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int x = a[t];
        int* p = a[t] > 0 ? default : &x;

        dst[t] = p == null ? 1 : *p;
    }
}
