// ReSharper disable All
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A 32-bit signed enum for conversion and narrow-integer differential tests.</summary>
public enum M1DiffIntEnum : int
{
    Zero = 0,
    One = 1,
    Min = int.MinValue,
    Max = int.MaxValue,
}

/// <summary>A 32-bit unsigned enum for conversion differential tests.</summary>
public enum M1DiffUIntEnum : uint
{
    Zero = 0,
    One = 1,
    Max = uint.MaxValue,
}

/// <summary>A 64-bit signed enum for conversion differential tests.</summary>
public enum M1DiffLongEnum : long
{
    Zero = 0,
    One = 1,
    Min = long.MinValue,
    Max = long.MaxValue,
}

/// <summary>A 64-bit unsigned enum for conversion differential tests.</summary>
public enum M1DiffULongEnum : ulong
{
    Zero = 0,
    One = 1,
    Max = ulong.MaxValue,
}

/// <summary>A 16-bit signed enum for conversion and narrow-enum differential tests.</summary>
public enum M1DiffShortEnum : short
{
    Zero = 0,
    One = 1,
    Min = short.MinValue,
    Max = short.MaxValue,
}

/// <summary>A 16-bit unsigned enum for conversion and narrow-enum differential tests.</summary>
public enum M1DiffUShortEnum : ushort
{
    Zero = 0,
    One = 1,
    Max = ushort.MaxValue,
}

/// <summary>An 8-bit signed enum for conversion and narrow-enum differential tests.</summary>
public enum M1DiffSByteEnum : sbyte
{
    Zero = 0,
    One = 1,
    Min = sbyte.MinValue,
    Max = sbyte.MaxValue,
}

/// <summary>An 8-bit unsigned enum for conversion and narrow-enum differential tests.</summary>
public enum M1DiffByteEnum : byte
{
    Zero = 0,
    One = 1,
    Max = byte.MaxValue,
}

/// <summary>
/// Differential kernels for conversions between floating-point, integer, enum, and <c>char</c> types with and
/// without explicit cast syntax.
/// </summary>
public static partial class M1DiffConversionKernels
{
    /// <summary>The columns of <see cref="ForeachFloatToIntMatrixKernel"/>.</summary>
    public static readonly string[] ForeachFloatToIntColumns =
    [
        "foreach(int in float)",
        "foreach(uint in float)",
        "foreach(long in float)",
        "foreach(ulong in float)",
        "foreach(short in float)",
        "foreach(ushort in float)",
        "foreach(sbyte in float)",
        "foreach(byte in float)",
        "foreach(char in float)",
        "foreach(IntEnum in float)",
        "foreach(ByteEnum in float)",
        "foreach(int in double)",
        "foreach(long in double)",
        "foreach(byte in double)",
        "foreach(int in yield float)",
    ];

    /// <summary>The columns of <see cref="EnumCastMatrixKernel"/>.</summary>
    public static readonly string[] EnumCastColumns =
    [
        "(IntEnum)f",
        "(UIntEnum)f",
        "(LongEnum)f",
        "(ULongEnum)f",
        "(ShortEnum)f",
        "(UShortEnum)f",
        "(SByteEnum)f",
        "(ByteEnum)f",
        "(IntEnum)d",
        "(LongEnum)d",
        "(ByteEnum)d",
        "(ByteEnum)i",
    ];

    /// <summary>The columns of <see cref="IntToFloatConversionMatrixKernel"/>.</summary>
    public static readonly string[] IntToFloatColumns =
    [
        "(float)i",
        "(float)u",
        "(float)l",
        "(float)ul",
        "float=l",
        "float=ul",
        "tuple float=ul",
    ];

    /// <summary>The columns of <see cref="IntToDoubleConversionMatrixKernel"/>.</summary>
    public static readonly string[] IntToDoubleColumns =
    [
        "(double)i",
        "(double)u",
        "(double)l",
        "(double)ul",
        "double=l",
        "double=ul",
    ];

    /// <summary>The columns of <see cref="CharScalarArgKernel"/>.</summary>
    public static readonly string[] CharScalarArgColumns =
    [
        "(int)cArg",
        "(int)cArr[t]",
        "cArg==0x1234",
    ];

    [GpuDeviceFunction]
    public static IEnumerable<float> YieldSingle(float v)
    {
        yield return v;
    }

    // C# §13.9.5: foreach (V v in x) inserts an explicit conversion (V)e on each element without a CastExpressionSyntax
    [GpuKernel]
    public static void ForeachFloatToIntMatrixKernel(float[] f, double[] d, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= f.Length)
        {
            return;
        }

        int o = t * 15;
        Span<float> sf = f.AsSpan().Slice(t, 1);
        Span<double> sd = d.AsSpan().Slice(t, 1);

        foreach (int x in sf)
        {
            r[o] = x;
        }

        foreach (uint x in sf)
        {
            r[o + 1] = x;
        }

        foreach (long x in sf)
        {
            r[o + 2] = x;
        }

        foreach (ulong x in sf)
        {
            r[o + 3] = (long)x;
        }

        foreach (short x in sf)
        {
            r[o + 4] = x;
        }

        foreach (ushort x in sf)
        {
            r[o + 5] = x;
        }

        foreach (sbyte x in sf)
        {
            r[o + 6] = x;
        }

        foreach (byte x in sf)
        {
            r[o + 7] = x;
        }

        foreach (char x in sf)
        {
            r[o + 8] = x;
        }

        foreach (M1DiffIntEnum x in sf)
        {
            r[o + 9] = (int)x;
        }

        foreach (M1DiffByteEnum x in sf)
        {
            r[o + 10] = (int)x;
        }

        foreach (int x in sd)
        {
            r[o + 11] = x;
        }

        foreach (long x in sd)
        {
            r[o + 12] = x;
        }

        foreach (byte x in sd)
        {
            r[o + 13] = x;
        }

        foreach (int x in YieldSingle(f[t]))
        {
            r[o + 14] = x;
        }
    }

    // Explicit casts from float, double, and int to enum types of every underlying integer width
    [GpuKernel]
    public static void EnumCastMatrixKernel(float[] f, double[] d, int[] i32, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= f.Length)
        {
            return;
        }

        int o = t * 12;
        float fv = f[t];
        double dv = d[t];
        int iv = i32[t];

        r[o] = (int)(M1DiffIntEnum)fv;
        r[o + 1] = (uint)(M1DiffUIntEnum)fv;
        r[o + 2] = (long)(M1DiffLongEnum)fv;
        r[o + 3] = (long)(ulong)(M1DiffULongEnum)fv;
        r[o + 4] = (short)(M1DiffShortEnum)fv;
        r[o + 5] = (ushort)(M1DiffUShortEnum)fv;
        r[o + 6] = (sbyte)(M1DiffSByteEnum)fv;
        r[o + 7] = (byte)(M1DiffByteEnum)fv;
        r[o + 8] = (int)(M1DiffIntEnum)dv;
        r[o + 9] = (long)(M1DiffLongEnum)dv;
        r[o + 10] = (byte)(M1DiffByteEnum)dv;
        r[o + 11] = (byte)(M1DiffByteEnum)iv;
    }

    // Integer -> float conversions (both explicit cast and implicit assignment/deconstruction)
    [GpuKernel]
    public static void IntToFloatConversionMatrixKernel(int[] i32, uint[] u, long[] l, ulong[] ul, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= i32.Length)
        {
            return;
        }

        int o = t * 7;
        int iv = i32[t];
        uint uv = u[t];
        long lv = l[t];
        ulong ulv = ul[t];

        float implL = lv;
        float implUl = ulv;
        float tupUl = 0f;
        float tupL = 0f;
        (tupUl, tupL) = (ulv, lv);

        r[o] = (float)iv;
        r[o + 1] = (float)uv;
        r[o + 2] = (float)lv;
        r[o + 3] = (float)ulv;
        r[o + 4] = implL;
        r[o + 5] = implUl;
        r[o + 6] = tupUl + (tupL - implL);
    }

    // Integer -> double conversions (both explicit cast and implicit assignment)
    [GpuKernel]
    public static void IntToDoubleConversionMatrixKernel(int[] i32, uint[] u, long[] l, ulong[] ul, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= i32.Length)
        {
            return;
        }

        int o = t * 6;
        int iv = i32[t];
        uint uv = u[t];
        long lv = l[t];
        ulong ulv = ul[t];

        double implL = lv;
        double implUl = ulv;

        r[o] = (double)iv;
        r[o + 1] = (double)uv;
        r[o + 2] = (double)lv;
        r[o + 3] = (double)ulv;
        r[o + 4] = implL;
        r[o + 5] = implUl;
    }

    // Scalar char kernel parameter vs char[] element (#971)
    [GpuKernel]
    public static void CharScalarArgKernel(char cArg, char[] cArr, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= cArr.Length)
        {
            return;
        }

        int o = t * 3;
        r[o] = cArg;
        r[o + 1] = cArr[t];
        r[o + 2] = cArg == '\u1234' ? 1 : 0;
    }
}
