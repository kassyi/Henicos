#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct SByteClampInputs
{
    [GpuReadOnly] public sbyte[] Values;
    [GpuReadOnly] public sbyte[] Minimums;
    [GpuReadOnly] public sbyte[] Maximums;
}

public struct ByteClampInputs
{
    [GpuReadOnly] public byte[] Values;
    [GpuReadOnly] public byte[] Minimums;
    [GpuReadOnly] public byte[] Maximums;
}

public struct Int16ClampInputs
{
    [GpuReadOnly] public short[] Values;
    [GpuReadOnly] public short[] Minimums;
    [GpuReadOnly] public short[] Maximums;
}

public struct UInt16ClampInputs
{
    [GpuReadOnly] public ushort[] Values;
    [GpuReadOnly] public ushort[] Minimums;
    [GpuReadOnly] public ushort[] Maximums;
}

public struct UInt32ClampInputs
{
    [GpuReadOnly] public uint[] Values;
    [GpuReadOnly] public uint[] Minimums;
    [GpuReadOnly] public uint[] Maximums;
}

public struct Int64ClampInputs
{
    [GpuReadOnly] public long[] Values;
    [GpuReadOnly] public long[] Minimums;
    [GpuReadOnly] public long[] Maximums;
}

public struct UInt64ClampInputs
{
    [GpuReadOnly] public ulong[] Values;
    [GpuReadOnly] public ulong[] Minimums;
    [GpuReadOnly] public ulong[] Maximums;
}

public struct IntegerSignInputs
{
    [GpuReadOnly] public sbyte[] Signed8Values;
    [GpuReadOnly] public short[] Signed16Values;
    [GpuReadOnly] public int[] Signed32Values;
    [GpuReadOnly] public long[] Signed64Values;
}

public struct MixedOperandInputs
{
    [GpuReadOnly] public long[] Signed64Values;
    [GpuReadOnly] public byte[] Unsigned8Values;
    [GpuReadOnly] public ushort[] Unsigned16Values;
    [GpuReadOnly] public sbyte[] Signed8Values;
    [GpuReadOnly] public uint[] Unsigned32Values;
    [GpuReadOnly] public int[] Signed32Values;
}

public struct NarrowResultInputs
{
    [GpuReadOnly] public byte[] Unsigned8Values;
    [GpuReadOnly] public sbyte[] Signed8Values;
    [GpuReadOnly] public ushort[] Unsigned16Values;
    [GpuReadOnly] public short[] Signed16Values;
}

public struct NarrowArgumentInputs
{
    [GpuReadOnly] public byte[] Unsigned8Values;
    [GpuReadOnly] public sbyte[] Signed8Values;
    [GpuReadOnly] public ushort[] Unsigned16Values;
}

/// <summary>
/// Differential kernels for integer <see cref="Math.Max(sbyte, sbyte)"/>, <see cref="Math.Min(sbyte, sbyte)"/> and
/// <see cref="Math.Clamp(sbyte, sbyte, sbyte)"/>, one kernel per type, <see cref="Math.Sign(sbyte)"/>, the overloads C#
/// picks for mixed operands, narrow results, and narrow arguments of BitOperations, <c>Math.Sign</c> and
/// <c>Math.Abs</c>.
/// </summary>
public static partial class IntegerMinMaxClampDifferentialKernels
{
    public static readonly string[] MaxMinColumns = ["max", "min"];

    public static readonly string[] SignColumns =
    [
        "sign_sbyte", "sign_int16", "sign_int32", "sign_int64", "sign_nint",
    ];

    public static readonly string[] MixedOperandColumns =
    [
        "maxLongLit", "clampLongLit", "maxUshortByte", "maxByteSbyte", "maxUintLit", "maxUintInt", "minNintLit",
        "maxNintInt", "clampByteLit",
    ];

    public static readonly string[] NarrowResultColumns =
    [
        "varMaxByteInc", "varMaxSbyteInc", "varClampUshortInc", "varMinShortInc", "refMaxByte", "refMinShort",
        "refMaxSbyte", "refClampUshort",
    ];

    public static readonly string[] NarrowArgumentColumns =
    [
        "isPow2Byte", "tzcSbyte", "roundUpUshort", "signByte", "absByte",
    ];

    [GpuKernel]
    public static void MaxMinSByteKernel(sbyte[] a, sbyte[] b, sbyte[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinByteKernel(byte[] a, byte[] b, byte[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinInt16Kernel(short[] a, short[] b, short[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinUInt16Kernel(ushort[] a, ushort[] b, ushort[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinUInt32Kernel(uint[] a, uint[] b, uint[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinInt64Kernel(long[] a, long[] b, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinUInt64Kernel(ulong[] a, ulong[] b, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t * 2] = Math.Max(a[t], b[t]);
        dst[t * 2 + 1] = Math.Min(a[t], b[t]);
    }

    [GpuKernel]
    public static void MaxMinIntPtrKernel(long[] a, long[] b, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        nint x = (nint)a[t];
        nint y = (nint)b[t];
        dst[t * 2] = Math.Max(x, y);
        dst[t * 2 + 1] = Math.Min(x, y);
    }

    [GpuKernel]
    public static void MaxMinUIntPtrKernel(ulong[] a, ulong[] b, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        nuint x = (nuint)a[t];
        nuint y = (nuint)b[t];
        dst[t * 2] = Math.Max(x, y);
        dst[t * 2 + 1] = Math.Min(x, y);
    }

    [GpuKernel]
    public static void ClampSByteKernel(SByteClampInputs inputs, sbyte[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampByteKernel(ByteClampInputs inputs, byte[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampInt16Kernel(Int16ClampInputs inputs, short[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampUInt16Kernel(UInt16ClampInputs inputs, ushort[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampUInt32Kernel(UInt32ClampInputs inputs, uint[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampInt64Kernel(Int64ClampInputs inputs, long[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampUInt64Kernel(UInt64ClampInputs inputs, ulong[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp(inputs.Values[t], inputs.Minimums[t], inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampIntPtrKernel(Int64ClampInputs inputs, long[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp((nint)inputs.Values[t], (nint)inputs.Minimums[t], (nint)inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void ClampUIntPtrKernel(UInt64ClampInputs inputs, ulong[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        results[t] = Math.Clamp((nuint)inputs.Values[t], (nuint)inputs.Minimums[t], (nuint)inputs.Maximums[t]);
    }

    [GpuKernel]
    public static void IntegerSignKernel(IntegerSignInputs inputs, int[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int baseIdx = t * 5;
        results[baseIdx + 0] = Math.Sign(inputs.Signed8Values[t]);
        results[baseIdx + 1] = Math.Sign(inputs.Signed16Values[t]);
        results[baseIdx + 2] = Math.Sign(inputs.Signed32Values[t]);
        results[baseIdx + 3] = Math.Sign(inputs.Signed64Values[t]);
        results[baseIdx + 4] = Math.Sign((nint)inputs.Signed64Values[t]);
    }

    // Each call is bound by C# to the overload in its comment; the arguments are converted to its parameter type
    [GpuKernel]
    public static void MixedOperandKernel(MixedOperandInputs inputs, long[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        var l = inputs.Signed64Values;
        var b = inputs.Unsigned8Values;
        var us = inputs.Unsigned16Values;
        var sb = inputs.Signed8Values;
        var u = inputs.Unsigned32Values;
        var iv = inputs.Signed32Values;
        var r = results;

        int o = t * 9;
        r[o] = Math.Max(l[t], 0); // Max(long, long)
        r[o + 1] = Math.Clamp(l[t], -5, 5); // Clamp(long, long, long)
        r[o + 2] = Math.Max(us[t], b[t]); // Max(ushort, ushort)
        r[o + 3] = Math.Max(b[t], sb[t]); // Max(short, short)
        r[o + 4] = Math.Max(u[t], 0); // Max(uint, uint)
        r[o + 5] = Math.Max(u[t], iv[t]); // Max(long, long)
        r[o + 6] = Math.Min((nint)l[t], 3); // Min(nint, nint)
        r[o + 7] = Math.Max((nint)l[t], iv[t]); // Max(nint, nint)
        r[o + 8] = Math.Clamp(b[t], (byte)10, (byte)200); // Clamp(byte, byte, byte)
    }

    [GpuDeviceFunction]
    public static void IncrementByte(ref byte x) => x++;

    [GpuDeviceFunction]
    public static void IncrementSByte(ref sbyte x) => x++;

    [GpuDeviceFunction]
    public static void IncrementInt16(ref short x) => x++;

    [GpuDeviceFunction]
    public static void IncrementUInt16(ref ushort x) => x++;

    // A narrow result kept in `var` keeps its type: `++` wraps in it, and `ref` passes a pointer to it, which a pointer
    // to an int would not convert to
    [GpuKernel]
    public static void NarrowResultKernel(NarrowResultInputs inputs, long[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        var b = inputs.Unsigned8Values;
        var sb = inputs.Signed8Values;
        var us = inputs.Unsigned16Values;
        var s = inputs.Signed16Values;
        var r = results;

        int o = t * 8;
        var mb = Math.Max(b[t], (byte)254);
        mb++;
        r[o] = mb;
        var ms = Math.Max(sb[t], (sbyte)0);
        ms++;
        r[o + 1] = ms;
        var cu = Math.Clamp(us[t], (ushort)0, (ushort)65535);
        cu++;
        r[o + 2] = cu;
        var mn = Math.Min(s[t], (short)32767);
        mn++;
        r[o + 3] = mn;
        var rb = Math.Max(b[t], (byte)254);
        IncrementByte(ref rb);
        r[o + 4] = rb;
        var rs = Math.Min(s[t], (short)32767);
        IncrementInt16(ref rs);
        r[o + 5] = rs;
        var rsb = Math.Max(sb[t], (sbyte)0);
        IncrementSByte(ref rsb);
        r[o + 6] = rsb;
        var rus = Math.Clamp(us[t], (ushort)0, (ushort)65535);
        IncrementUInt16(ref rus);
        r[o + 7] = rus;
    }

    // C# binds a narrow argument to a wider overload: the comment names it
    [GpuKernel]
    public static void NarrowArgumentKernel(NarrowArgumentInputs inputs, long[] results, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        var b = inputs.Unsigned8Values;
        var sb = inputs.Signed8Values;
        var us = inputs.Unsigned16Values;
        var r = results;

        int o = t * 5;
        r[o] = BitOperations.IsPow2(b[t]) ? 1 : 0; // IsPow2(int)
        r[o + 1] = BitOperations.TrailingZeroCount(sb[t]); // TrailingZeroCount(int)
        r[o + 2] = BitOperations.RoundUpToPowerOf2(us[t]); // RoundUpToPowerOf2(uint)
        r[o + 3] = Math.Sign(b[t]); // Sign(short)
        r[o + 4] = Math.Abs(b[t]); // Abs(short)
    }
}
