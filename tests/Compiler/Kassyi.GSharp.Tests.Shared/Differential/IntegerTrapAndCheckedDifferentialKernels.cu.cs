#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for Lane D5 Stage 2: integer division/remainder by zero (<c>/</c>, <c>%</c>, <c>/=</c>, <c>%=</c>),
/// <c>int.MinValue / -1</c> and <c>long.MinValue / -1L</c> overflow vs narrow <c>short.MinValue / (short)-1</c> promotion,
/// <c>Math.Abs(int.MinValue)</c> (#947), shift-count masking across negative and &gt;= width counts (#722), and
/// <c>unchecked</c> arithmetic wrap-around + IEEE 754 float division by zero + float-to-integer saturation (#724, #890).
/// </summary>
public static partial class IntegerTrapAndCheckedDifferentialKernels
{
    private const int ConstUncheckedIntMinDivMinusOne = unchecked(int.MinValue / -1);
    private const long ConstUncheckedLongMinDivMinusOne = unchecked(long.MinValue / -1L);

    public static readonly string[] DivideByZeroColumns =
    [
        "i_div",
        "i_rem",
        "u_div",
        "u_rem",
        "l_div_low",
        "ul_div_low",
        "s_compound_div",
        "sb_compound_rem",
    ];

    public static readonly string[] SignedMinDivColumns =
    [
        "i_quot",
        "i_rem",
        "l_quot_low",
        "l_rem_low",
        "short_min_div_m1",
        "sbyte_min_div_m1",
        "const_unchecked_int_min_div_m1",
        "const_unchecked_long_min_div_m1_low",
    ];

    public static readonly string[] MathAbsIntColumns =
    [
        "abs_int",
    ];

    public static readonly string[] MathAbsFloatColumns =
    [
        "abs_float",
    ];

    public static readonly string[] MathAbsDoubleColumns =
    [
        "abs_double",
    ];

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

    public static readonly string[] UncheckedAndFloatSaturationColumns =
    [
        "i_add_wrap",
        "i_sub_wrap",
        "i_mul_wrap",
        "i_neg_min",
        "u_dec_zero",
        "b_inc_max",
        "s_compound_add",
        "f_div_inf_flags",
        "d_div_inf_flags",
        "f_to_int_sat",
        "f_to_uint_sat",
        "f_to_byte_sat",
        "f_to_sbyte_sat",
        "d_to_int_sat",
        "d_to_long_sat_low",
    ];

    public static readonly string[] FloatDivZeroColumns =
    [
        "f_div_zero",
        "f_rem_zero",
    ];

    public static readonly string[] DoubleDivZeroColumns =
    [
        "d_div_zero",
        "d_rem_zero",
    ];

    /// <summary>
    /// I-01: Integer division and remainder (<c>/</c>, <c>%</c>, <c>/=</c>, <c>%=</c>) across signed and unsigned
    /// integer types. Seed 0 uses non-zero divisors across all 4 paths; fault seeds inject a zero divisor in one
    /// category at a time.
    /// </summary>
    [GpuKernel]
    public static void IntegerDivideByZeroKernel(
        int[] iNums,
        int[] iDenoms,
        int[] iRemDenoms,
        uint[] uNums,
        uint[] uDenoms,
        long[] lNums,
        long[] lDenoms,
        short[] sNums,
        short[] sDenoms,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int iNum = iNums[t];
        int iDen = iDenoms[t];
        int iRemDen = iRemDenoms[t];
        int iDiv = iNum / iDen;
        int iRem = iNum % iRemDen;

        uint uNum = uNums[t];
        uint uDen = uDenoms[t];
        uint uDiv = uNum / uDen;
        uint uRem = uNum % uDen;

        long lNum = lNums[t];
        long lDen = lDenoms[t];
        long lDiv = lNum / lDen;
        ulong ulNum = (ulong)uNum + 100UL;
        ulong ulDen = (ulong)uDen;
        ulong ulDiv = ulNum / ulDen;

        short sVal = sNums[t];
        short sDen = sDenoms[t];
        sVal /= sDen;

        sbyte sbVal = (sbyte)(sNums[t] & 0x7F);
        sbyte sbDen = (sbyte)sDen;
        sbVal %= sbDen;

        int baseIdx = t * 8;
        dst[baseIdx + 0] = iDiv;
        dst[baseIdx + 1] = iRem;
        dst[baseIdx + 2] = (int)uDiv;
        dst[baseIdx + 3] = (int)uRem;
        dst[baseIdx + 4] = (int)lDiv;
        dst[baseIdx + 5] = (int)ulDiv;
        dst[baseIdx + 6] = sVal;
        dst[baseIdx + 7] = sbVal;
    }

    /// <summary>
    /// I-02: Signed two's-complement <c>MinValue / -1</c> and <c>MinValue % -1</c> for <c>int</c> and <c>long</c>,
    /// alongside non-trapping narrow <c>short.MinValue / (short)-1 == 32768</c>, <c>sbyte.MinValue / (sbyte)-1 == 128</c>,
    /// and compile-time <c>unchecked(int.MinValue / -1)</c>.
    /// </summary>
    [GpuKernel]
    public static void SignedMinValueDivideByMinusOneKernel(
        int[] iNums,
        int[] iDivDenoms,
        int[] iRemDenoms,
        long[] lNums,
        long[] lDivDenoms,
        long[] lRemDenoms,
        short[] sVals,
        sbyte[] sbVals,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int iQuot = iNums[t] / iDivDenoms[t];
        int iRem = iNums[t] % iRemDenoms[t];
        long lQuot = lNums[t] / lDivDenoms[t];
        long lRem = lNums[t] % lRemDenoms[t];

        // Narrow signed types promote to int before '/', so short.MinValue / (short)-1 == +32768 (no trap).
        short sMin = sVals[t];
        short sMinusOne = -1;
        int shortPromotedQuot = sMin / sMinusOne;

        sbyte sbMin = sbVals[t];
        sbyte sbMinusOne = -1;
        int sbytePromotedQuot = sbMin / sbMinusOne;

        int constUncheckedInt = ConstUncheckedIntMinDivMinusOne;
        long constUncheckedLong = ConstUncheckedLongMinDivMinusOne;

        int baseIdx = t * 8;
        dst[baseIdx + 0] = iQuot;
        dst[baseIdx + 1] = iRem;
        dst[baseIdx + 2] = (int)lQuot;
        dst[baseIdx + 3] = (int)lRem;
        dst[baseIdx + 4] = shortPromotedQuot;
        dst[baseIdx + 5] = sbytePromotedQuot;
        dst[baseIdx + 6] = constUncheckedInt;
        dst[baseIdx + 7] = (int)(constUncheckedLong >> 32);
    }

    /// <summary>
    /// I-03: <see cref="Math.Abs(int)"/> on <c>int.MinValue</c> (throws <see cref="OverflowException"/> in C#, #947)
    /// and <c>Math.Abs(float)</c> / <c>Math.Abs(double)</c> across signed zeros, infinities, and NaN.
    /// </summary>
    [GpuKernel]
    public static void MathAbsMinValueKernel(
        int[] iVals,
        float[] fVals,
        double[] dVals,
        int[] iDst,
        float[] fDst,
        double[] dDst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        iDst[t] = Math.Abs(iVals[t]);
        fDst[t] = Math.Abs(fVals[t]);
        dDst[t] = Math.Abs(dVals[t]);
    }

    /// <summary>
    /// I-04: Shift count masking (<c>&amp; 31</c> for 32-bit/narrow types, <c>&amp; 63</c> for 64-bit types) across
    /// negative counts, counts &gt;= bit width, and unsigned right shift <c>&gt;&gt;&gt;</c> (#722).
    /// </summary>
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

    /// <summary>
    /// I-05: Default <c>unchecked</c> integer wrap-around (<c>+</c>, <c>-</c>, <c>*</c>, <c>-x</c>, <c>++</c>,
    /// <c>--</c>, <c>+=</c>), IEEE 754 floating-point division/remainder by zero (<c>±Inf</c>, <c>NaN</c>), and
    /// floating-point to integer saturating/truncating casts (#724, #890).
    /// </summary>
    [GpuKernel]
    public static void UncheckedOverflowAndFloatSaturationMatrixKernel(
        int[] iLeft,
        int[] iRight,
        float[] fVals,
        double[] dVals,
        int[] dst,
        float[] fDst,
        double[] dDst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = iLeft[t];
        int b = iRight[t];

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

        float fv = fVals[t];
        float fZero = 0.0f;
        float fQuot = fv / fZero;
        float fZeroByZero = fZero / fZero;
        int fDivFlags =
            (fQuot == float.PositiveInfinity ? 1 : 0) |
            (fQuot == float.NegativeInfinity ? 2 : 0);

        double dv = dVals[t];
        double dZero = 0.0;
        double dQuot = dv / dZero;
        double dZeroByZero = dZero / dZero;
        int dDivFlags =
            (dQuot == double.PositiveInfinity ? 1 : 0) |
            (dQuot == double.NegativeInfinity ? 2 : 0);

        int fToInt = (int)fv;
        uint fToUInt = (uint)fv;
        byte fToByte = (byte)fv;
        sbyte fToSByte = (sbyte)fv;
        int dToInt = (int)dv;
        long dToLong = (long)dv;

        int baseIdx = t * 15;
        dst[baseIdx + 0] = addWrap;
        dst[baseIdx + 1] = subWrap;
        dst[baseIdx + 2] = mulWrap;
        dst[baseIdx + 3] = negMin;
        dst[baseIdx + 4] = (int)uZero;
        dst[baseIdx + 5] = bMax;
        dst[baseIdx + 6] = sAcc;
        dst[baseIdx + 7] = fDivFlags;
        dst[baseIdx + 8] = dDivFlags;
        dst[baseIdx + 9] = fToInt;
        dst[baseIdx + 10] = (int)fToUInt;
        dst[baseIdx + 11] = fToByte;
        dst[baseIdx + 12] = fToSByte;
        dst[baseIdx + 13] = dToInt;
        dst[baseIdx + 14] = (int)(dToLong ^ (dToLong >> 32));

        int fBase = t * 2;
        fDst[fBase + 0] = fQuot;
        fDst[fBase + 1] = fZeroByZero;
        dDst[fBase + 0] = dQuot;
        dDst[fBase + 1] = dZeroByZero;
    }
}
