// ReSharper disable All
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A 32-bit signed enum for conversion and narrow-integer differential tests.</summary>
public enum DiffIntEnum : int
{
    Zero = 0,
    One = 1,
    Min = int.MinValue,
    Max = int.MaxValue,
}

/// <summary>A 32-bit unsigned enum for conversion differential tests.</summary>
public enum DiffUIntEnum : uint
{
    Zero = 0,
    One = 1,
    Max = uint.MaxValue,
}

/// <summary>A 64-bit signed enum for conversion differential tests.</summary>
public enum DiffLongEnum : long
{
    Zero = 0,
    One = 1,
    Min = long.MinValue,
    Max = long.MaxValue,
}

/// <summary>A 64-bit unsigned enum for conversion differential tests.</summary>
public enum DiffULongEnum : ulong
{
    Zero = 0,
    One = 1,
    Max = ulong.MaxValue,
}

/// <summary>A 16-bit signed enum for conversion and narrow-enum differential tests.</summary>
public enum DiffShortEnum : short
{
    Zero = 0,
    One = 1,
    Min = short.MinValue,
    Max = short.MaxValue,
}

/// <summary>A 16-bit unsigned enum for conversion and narrow-enum differential tests.</summary>
public enum DiffUShortEnum : ushort
{
    Zero = 0,
    One = 1,
    Max = ushort.MaxValue,
}

/// <summary>An 8-bit signed enum for conversion and narrow-enum differential tests.</summary>
public enum DiffSByteEnum : sbyte
{
    Zero = 0,
    One = 1,
    Min = sbyte.MinValue,
    Max = sbyte.MaxValue,
}

/// <summary>An 8-bit unsigned enum for conversion and narrow-enum differential tests.</summary>
public enum DiffByteEnum : byte
{
    Zero = 0,
    One = 1,
    Max = byte.MaxValue,
}

/// <summary>
/// Differential kernels for conversions between floating-point, integer, enum, and <c>char</c> types with and
/// without explicit cast syntax.
/// </summary>
public static partial class ConversionDifferentialKernels
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

    /// <summary>The signed integer columns of <see cref="DirectFloatDoubleCastMatrixKernel"/>.</summary>
    public static readonly string[] DirectSignedCastColumns =
    [
        "(sbyte)f",
        "(short)f",
        "(int)f",
        "(long)f",
        "(sbyte)d",
        "(short)d",
        "(int)d",
        "(long)d",
        "(long)(float)i64",
        "(long)(double)i64",
    ];

    /// <summary>The unsigned integer columns of <see cref="DirectFloatDoubleCastMatrixKernel"/>.</summary>
    public static readonly string[] DirectUnsignedCastColumns =
    [
        "(byte)f",
        "(ushort)f",
        "(char)f",
        "(uint)f",
        "(ulong)f",
        "(byte)d",
        "(ushort)d",
        "(char)d",
        "(uint)d",
        "(ulong)d",
    ];

    /// <summary>The <see cref="float"/> columns of <see cref="DirectFloatDoubleCastMatrixKernel"/>.</summary>
    public static readonly string[] DirectFloatCastColumns =
    [
        "(float)d",
        "(float)i64",
        "(float)u64",
    ];

    /// <summary>The <see cref="double"/> columns of <see cref="DirectFloatDoubleCastMatrixKernel"/>.</summary>
    public static readonly string[] DirectDoubleCastColumns =
    [
        "(double)f",
        "(double)i64",
        "(double)u64",
    ];

    /// <summary>The signed columns of <see cref="IntegerCastAndShiftMaskMatrixKernel"/>.</summary>
    public static readonly string[] IntegerSignedCastShiftColumns =
    [
        "(long)sb",
        "(long)sh16",
        "(long)iv",
        "(sbyte)lv",
        "(short)ulv",
        "(int)ulv",
        "iv<<s",
        "iv>>s",
        "iv>>>s",
        "lv<<s",
        "lv>>s",
        "lv>>>s",
        "-sb+(-sh16)+(-b)",
        "compI>>=s",
    ];

    /// <summary>The unsigned columns of <see cref="IntegerCastAndShiftMaskMatrixKernel"/>.</summary>
    public static readonly string[] IntegerUnsignedCastShiftColumns =
    [
        "(ulong)b",
        "(ulong)ush16",
        "(ulong)ch",
        "(ulong)uv",
        "(ulong)sb",
        "(ulong)sh16",
        "(ulong)iv",
        "uv<<s",
        "uv>>s",
        "(ulv<<s)^(ulv>>s)",
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

        foreach (DiffIntEnum x in sf)
        {
            r[o + 9] = (int)x;
        }

        foreach (DiffByteEnum x in sf)
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

        r[o] = (int)(DiffIntEnum)fv;
        r[o + 1] = (uint)(DiffUIntEnum)fv;
        r[o + 2] = (long)(DiffLongEnum)fv;
        r[o + 3] = (long)(ulong)(DiffULongEnum)fv;
        r[o + 4] = (short)(DiffShortEnum)fv;
        r[o + 5] = (ushort)(DiffUShortEnum)fv;
        r[o + 6] = (sbyte)(DiffSByteEnum)fv;
        r[o + 7] = (byte)(DiffByteEnum)fv;
        r[o + 8] = (int)(DiffIntEnum)dv;
        r[o + 9] = (long)(DiffLongEnum)dv;
        r[o + 10] = (byte)(DiffByteEnum)dv;
        r[o + 11] = (byte)(DiffByteEnum)iv;
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

    // Direct casts between float/double and all 9 integer types (#724, #890), 64-bit integer rounding ties, and double <-> float
    [GpuKernel]
    public static void DirectFloatDoubleCastMatrixKernel(
        float[] f, double[] d, long[] i64, ulong[] u64,
        long[] rSigned, ulong[] rUnsigned, float[] rFloat, double[] rDouble)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= f.Length)
        {
            return;
        }

        float fv = f[t];
        double dv = d[t];

        int os = t * 10;
        rSigned[os] = (sbyte)fv;
        rSigned[os + 1] = (short)fv;
        rSigned[os + 2] = (int)fv;
        rSigned[os + 3] = (long)fv;
        rSigned[os + 4] = (sbyte)dv;
        rSigned[os + 5] = (short)dv;
        rSigned[os + 6] = (int)dv;
        rSigned[os + 7] = (long)dv;
        rSigned[os + 8] = (long)(float)i64[t];
        rSigned[os + 9] = (long)(double)i64[t];

        int ou = t * 10;
        rUnsigned[ou] = (byte)fv;
        rUnsigned[ou + 1] = (ushort)fv;
        rUnsigned[ou + 2] = (char)fv;
        rUnsigned[ou + 3] = (uint)fv;
        rUnsigned[ou + 4] = (ulong)fv;
        rUnsigned[ou + 5] = (byte)dv;
        rUnsigned[ou + 6] = (ushort)dv;
        rUnsigned[ou + 7] = (char)dv;
        rUnsigned[ou + 8] = (uint)dv;
        rUnsigned[ou + 9] = (ulong)dv;

        int of = t * 3;
        rFloat[of] = (float)dv;
        rFloat[of + 1] = (float)i64[t];
        rFloat[of + 2] = (float)u64[t];

        int od = t * 3;
        rDouble[od] = (double)fv;
        rDouble[od + 1] = (double)i64[t];
        rDouble[od + 2] = (double)u64[t];
    }

    // Integer-to-integer widening/narrowing casts, shift count masks (s & 31 / s & 63), sign extension, and unary minus promotion
    [GpuKernel]
    public static void IntegerCastAndShiftMaskMatrixKernel(
        int[] i32, uint[] u32, long[] i64, ulong[] u64, int[] sh,
        long[] rSigned, ulong[] rUnsigned)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= i32.Length)
        {
            return;
        }

        int iv = i32[t];
        uint uv = u32[t];
        long lv = i64[t];
        ulong ulv = u64[t];
        int s = sh[t];

        sbyte sb = (sbyte)iv;
        byte b = (byte)uv;
        short sh16 = (short)iv;
        ushort ush16 = (ushort)uv;
        char ch = (char)uv;

        int os = t * 14;
        rSigned[os] = (long)sb;
        rSigned[os + 1] = (long)sh16;
        rSigned[os + 2] = (long)iv;
        rSigned[os + 3] = (sbyte)lv;
        rSigned[os + 4] = (short)ulv;
        rSigned[os + 5] = (int)ulv;
        rSigned[os + 6] = iv << s;
        rSigned[os + 7] = iv >> s;
        rSigned[os + 8] = iv >>> s;
        rSigned[os + 9] = lv << s;
        rSigned[os + 10] = lv >> s;
        rSigned[os + 11] = lv >>> s;
        rSigned[os + 12] = -sb + (-sh16) + (-b);
        int compI = iv;
        compI >>= s;
        rSigned[os + 13] = compI;

        int ou = t * 10;
        rUnsigned[ou] = (ulong)b;
        rUnsigned[ou + 1] = (ulong)ush16;
        rUnsigned[ou + 2] = (ulong)ch;
        rUnsigned[ou + 3] = (ulong)uv;
        rUnsigned[ou + 4] = (ulong)sb;
        rUnsigned[ou + 5] = (ulong)sh16;
        rUnsigned[ou + 6] = (ulong)iv;
        rUnsigned[ou + 7] = uv << s;
        rUnsigned[ou + 8] = uv >> s;
        rUnsigned[ou + 9] = (ulv << s) ^ (ulv >> s);
    }
}
