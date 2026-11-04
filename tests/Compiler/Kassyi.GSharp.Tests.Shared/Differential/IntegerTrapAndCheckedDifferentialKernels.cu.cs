#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for integer division/remainder by zero (<c>/</c>, <c>%</c>, <c>/=</c>, <c>%=</c>),
/// <c>int.MinValue / -1</c> and <c>long.MinValue / -1L</c> overflow vs narrow <c>short.MinValue / (short)-1</c> promotion (#718),
/// <c>Math.Abs</c> on signed min values and floats (#947), shift-count masking across negative and &gt;= width counts (#722), and
/// <c>unchecked</c> arithmetic wrap-around, IEEE 754 float division by zero, and float-to-integer conversion (#724, #890).
/// </summary>
public static partial class IntegerTrapAndCheckedDifferentialKernels
{
    public static readonly string[] ShiftMaskBoundaryColumns =
    [
        "i_shl",
        "i_shr",
        "i_ushr",
        "u_shl",
        "u_shr",
        "u_ushr",
        "l_shl_low",
        "l_shr_low",
        "l_ushr_low",
        "ul_shl_low",
        "ul_shr_low",
        "ul_ushr_low",
        "s_shl",
        "sb_shr",
        "i_compound_shl",
        "i_compound_ushr",
    ];

    public static readonly string[] UncheckedIntegerWrapColumns =
    [
        "i_add_wrap",
        "i_sub_wrap",
        "i_mul_wrap",
        "i_neg_min",
        "u_dec_zero",
        "b_inc_max",
        "s_compound_add",
    ];

    public static readonly string[] FloatToIntegerConversionColumns =
    [
        "f_to_int_sat",
        "f_to_uint_sat",
        "f_to_byte_sat",
        "f_to_sbyte_sat",
        "d_to_int_sat",
        "d_to_long_sat_low",
    ];

    [GpuKernel]
    public static void Int32DivideKernel(int[] nums, int[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] / denoms[t];
    }

    [GpuKernel]
    public static void Int32RemainderKernel(int[] nums, int[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] % denoms[t];
    }

    [GpuKernel]
    public static void UInt32DivideKernel(uint[] nums, uint[] denoms, uint[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] / denoms[t];
    }

    [GpuKernel]
    public static void UInt32RemainderKernel(uint[] nums, uint[] denoms, uint[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] % denoms[t];
    }

    [GpuKernel]
    public static void Int64DivideKernel(long[] nums, long[] denoms, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] / denoms[t];
    }

    [GpuKernel]
    public static void UInt64DivideKernel(ulong[] nums, ulong[] denoms, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] / denoms[t];
    }

    [GpuKernel]
    public static void Int16CompoundDivideKernel(short[] nums, short[] denoms, short[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        short val = nums[t];
        val /= denoms[t];
        dst[t] = val;
    }

    [GpuKernel]
    public static void SByteCompoundRemainderKernel(sbyte[] nums, sbyte[] denoms, sbyte[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        sbyte val = nums[t];
        val %= denoms[t];
        dst[t] = val;
    }

    [GpuKernel]
    public static void Int32MinValueDivideKernel(int[] nums, int[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] / denoms[t];
    }

    [GpuKernel]
    public static void Int32MinValueRemainderKernel(int[] nums, int[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] % denoms[t];
    }

    [GpuKernel]
    public static void Int64MinValueDivideKernel(long[] nums, long[] denoms, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] / denoms[t];
    }

    [GpuKernel]
    public static void Int64MinValueRemainderKernel(long[] nums, long[] denoms, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = nums[t] % denoms[t];
    }

    [GpuKernel]
    public static void ShortMinValueDivideKernel(short[] nums, short[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        short sMin = nums[t];
        short sMinusOne = denoms[t];
        dst[t] = sMin / sMinusOne;
    }

    [GpuKernel]
    public static void SByteMinValueDivideKernel(sbyte[] nums, sbyte[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        sbyte sbMin = nums[t];
        sbyte sbMinusOne = denoms[t];
        dst[t] = sbMin / sbMinusOne;
    }

    [GpuKernel]
    public static void MathAbsInt32Kernel(int[] vals, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Abs(vals[t]);
    }

    [GpuKernel]
    public static void MathAbsSingleKernel(float[] vals, float[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Abs(vals[t]);
    }

    [GpuKernel]
    public static void MathAbsDoubleKernel(double[] vals, double[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Abs(vals[t]);
    }

    [GpuKernel]
    public static void ShiftCountMaskBoundaryMatrixKernel(
        int[] iVals,
        uint[] uVals,
        long[] lVals,
        ulong[] ulVals,
        short[] sVals,
        sbyte[] sbVals,
        int[] counts,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int c = counts[t];
        int iv = iVals[t];
        uint uv = uVals[t];
        long lv = lVals[t];
        ulong ulv = ulVals[t];
        short sv = sVals[t];
        sbyte sbv = sbVals[t];

        int iShl = iv << c;
        int iShr = iv >> c;
        int iUshr = iv >>> c;

        uint uShl = uv << c;
        uint uShr = uv >> c;
        uint uUshr = uv >>> c;

        long lShl = lv << c;
        long lShr = lv >> c;
        long lUshr = lv >>> c;

        ulong ulShl = ulv << c;
        ulong ulShr = ulv >> c;
        ulong ulUshr = ulv >>> c;

        int sShl = sv << c;
        int sbShr = sbv >> c;

        int compShl = iv;
        compShl <<= c;
        int compUshr = iv;
        compUshr >>>= c;

        int baseIdx = t * 16;
        dst[baseIdx + 0] = iShl;
        dst[baseIdx + 1] = iShr;
        dst[baseIdx + 2] = iUshr;
        dst[baseIdx + 3] = (int)uShl;
        dst[baseIdx + 4] = (int)uShr;
        dst[baseIdx + 5] = (int)uUshr;
        dst[baseIdx + 6] = (int)(lShl ^ (lShl >> 32));
        dst[baseIdx + 7] = (int)(lShr ^ (lShr >> 32));
        dst[baseIdx + 8] = (int)(lUshr ^ (lUshr >> 32));
        dst[baseIdx + 9] = (int)(ulShl ^ (ulShl >> 32));
        dst[baseIdx + 10] = (int)(ulShr ^ (ulShr >> 32));
        dst[baseIdx + 11] = (int)(ulUshr ^ (ulUshr >> 32));
        dst[baseIdx + 12] = sShl;
        dst[baseIdx + 13] = sbShr;
        dst[baseIdx + 14] = compShl;
        dst[baseIdx + 15] = compUshr;
    }

    [GpuKernel]
    public static void UncheckedIntegerWrapKernel(int[] left, int[] right, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = left[t];
        int b = right[t];

        int addWrap = a + b;
        int subWrap = a - b;
        int mulWrap = a * b;
        int negMin = -a;

        uint uZero = (uint)(a & 1);
        uZero--;

        byte bMax = (byte)(254 + (a & 1));
        bMax++;

        short sAcc = (short)(a & 0x7FFF);
        sAcc += (short)(b & 0x7FFF);

        int baseIdx = t * 7;
        dst[baseIdx + 0] = addWrap;
        dst[baseIdx + 1] = subWrap;
        dst[baseIdx + 2] = mulWrap;
        dst[baseIdx + 3] = negMin;
        dst[baseIdx + 4] = (int)uZero;
        dst[baseIdx + 5] = bMax;
        dst[baseIdx + 6] = sAcc;
    }

    [GpuKernel]
    public static void FloatDivisionByZeroKernel(float[] fVals, double[] dVals, float[] fDst, double[] dDst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        float fv = fVals[t];
        float fZero = 0.0f;
        float fQuot = fv / fZero;
        float fZeroByZero = fZero / fZero;

        double dv = dVals[t];
        double dZero = 0.0;
        double dQuot = dv / dZero;
        double dZeroByZero = dZero / dZero;

        int fBase = t * 2;
        fDst[fBase + 0] = fQuot;
        fDst[fBase + 1] = fZeroByZero;

        int dBase = t * 2;
        dDst[dBase + 0] = dQuot;
        dDst[dBase + 1] = dZeroByZero;
    }

    [GpuKernel]
    public static void FloatToIntegerConversionKernel(float[] fVals, double[] dVals, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        float fv = fVals[t];
        double dv = dVals[t];

        int fToInt = (int)fv;
        uint fToUInt = (uint)fv;
        byte fToByte = (byte)fv;
        sbyte fToSByte = (sbyte)fv;
        int dToInt = (int)dv;
        long dToLong = (long)dv;

        int baseIdx = t * 6;
        dst[baseIdx + 0] = fToInt;
        dst[baseIdx + 1] = (int)fToUInt;
        dst[baseIdx + 2] = fToByte;
        dst[baseIdx + 3] = fToSByte;
        dst[baseIdx + 4] = dToInt;
        dst[baseIdx + 5] = (int)(dToLong ^ (dToLong >> 32));
    }
}
