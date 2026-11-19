#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

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
    public static void ClampSByteKernel(sbyte[] value, sbyte[] min, sbyte[] max, sbyte[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampByteKernel(byte[] value, byte[] min, byte[] max, byte[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampInt16Kernel(short[] value, short[] min, short[] max, short[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampUInt16Kernel(ushort[] value, ushort[] min, ushort[] max, ushort[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampUInt32Kernel(uint[] value, uint[] min, uint[] max, uint[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampInt64Kernel(long[] value, long[] min, long[] max, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampUInt64Kernel(ulong[] value, ulong[] min, ulong[] max, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(value[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void ClampIntPtrKernel(long[] value, long[] min, long[] max, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp((nint)value[t], (nint)min[t], (nint)max[t]);
    }

    [GpuKernel]
    public static void ClampUIntPtrKernel(ulong[] value, ulong[] min, ulong[] max, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp((nuint)value[t], (nuint)min[t], (nuint)max[t]);
    }

    [GpuKernel]
    public static void IntegerSignKernel(
        sbyte[] sbV,
        short[] sV,
        int[] iV,
        long[] lV,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int baseIdx = t * 5;
        dst[baseIdx + 0] = Math.Sign(sbV[t]);
        dst[baseIdx + 1] = Math.Sign(sV[t]);
        dst[baseIdx + 2] = Math.Sign(iV[t]);
        dst[baseIdx + 3] = Math.Sign(lV[t]);
        dst[baseIdx + 4] = Math.Sign((nint)lV[t]);
    }

    // Each call is bound by C# to the overload in its comment; the arguments are converted to its parameter type
    [GpuKernel]
    public static void MixedOperandKernel(
        long[] l,
        byte[] b,
        ushort[] us,
        sbyte[] sb,
        uint[] u,
        int[] iv,
        long[] r,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

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
    public static void NarrowResultKernel(byte[] b, sbyte[] sb, ushort[] us, short[] s, long[] r, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

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
    public static void NarrowArgumentKernel(byte[] b, sbyte[] sb, ushort[] us, long[] r, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int o = t * 5;
        r[o] = BitOperations.IsPow2(b[t]) ? 1 : 0; // IsPow2(int)
        r[o + 1] = BitOperations.TrailingZeroCount(sb[t]); // TrailingZeroCount(int)
        r[o + 2] = BitOperations.RoundUpToPowerOf2(us[t]); // RoundUpToPowerOf2(uint)
        r[o + 3] = Math.Sign(b[t]); // Sign(short)
        r[o + 4] = Math.Abs(b[t]); // Abs(short)
    }
}
