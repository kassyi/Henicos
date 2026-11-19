#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for integer <see cref="Math.Max(sbyte, sbyte)"/>, <see cref="Math.Min(sbyte, sbyte)"/>,
/// <see cref="Math.Clamp(sbyte, sbyte, sbyte)"/> and <see cref="Math.Sign(sbyte)"/>, the overloads C# picks for mixed
/// operands, and narrow results passed by <see langword="ref"/>.
/// </summary>
public static partial class IntegerMinMaxClampDifferentialKernels
{
    public static readonly string[] MaxMinColumns = ["max", "min"];

    public static readonly string[] MixedOperandColumns =
    [
        "maxLongLit", "clampLongLit", "maxUshortByte", "maxByteSbyte", "maxUintLit", "maxUintInt", "minNintLit",
        "maxNintInt", "clampByteLit", "varMaxByteInc", "varMaxSbyteInc", "varClampUshortInc", "varMinShortInc",
        "isPow2Byte", "tzcSbyte", "roundUpUshort", "signByte", "absByte", "refMaxByte", "refMinShort",
        "refMaxSbyte", "refClampUshort",
    ];

    public static readonly string[] SignColumns =
    [
        "sign_sbyte", "sign_int16", "sign_int32", "sign_int64", "sign_nint",
    ];

    [GpuKernel]
    public static void NarrowMaxMinKernel(
        sbyte[] sbA,
        sbyte[] sbB,
        byte[] bA,
        byte[] bB,
        short[] sA,
        short[] sB,
        ushort[] usA,
        ushort[] usB,
        sbyte[] sbOut,
        byte[] bOut,
        short[] sOut,
        ushort[] usOut,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int pairBase = t * 2;
        sbyte sbMax = Math.Max(sbA[t], sbB[t]);
        sbyte sbMin = Math.Min(sbA[t], sbB[t]);
        sbOut[pairBase + 0] = sbMax;
        sbOut[pairBase + 1] = sbMin;

        byte bMax = Math.Max(bA[t], bB[t]);
        byte bMin = Math.Min(bA[t], bB[t]);
        bOut[pairBase + 0] = bMax;
        bOut[pairBase + 1] = bMin;

        short sMax = Math.Max(sA[t], sB[t]);
        short sMin = Math.Min(sA[t], sB[t]);
        sOut[pairBase + 0] = sMax;
        sOut[pairBase + 1] = sMin;

        ushort usMax = Math.Max(usA[t], usB[t]);
        ushort usMin = Math.Min(usA[t], usB[t]);
        usOut[pairBase + 0] = usMax;
        usOut[pairBase + 1] = usMin;
    }

    [GpuKernel]
    public static void WideMaxMinKernel(
        uint[] uA,
        uint[] uB,
        long[] lA,
        long[] lB,
        ulong[] ulA,
        ulong[] ulB,
        uint[] uOut,
        long[] lOut,
        ulong[] ulOut,
        long[] nOut,
        ulong[] nuOut,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int pairBase = t * 2;
        uOut[pairBase + 0] = Math.Max(uA[t], uB[t]);
        uOut[pairBase + 1] = Math.Min(uA[t], uB[t]);

        lOut[pairBase + 0] = Math.Max(lA[t], lB[t]);
        lOut[pairBase + 1] = Math.Min(lA[t], lB[t]);

        ulOut[pairBase + 0] = Math.Max(ulA[t], ulB[t]);
        ulOut[pairBase + 1] = Math.Min(ulA[t], ulB[t]);

        nint na = (nint)lA[t];
        nint nb = (nint)lB[t];
        nOut[pairBase + 0] = Math.Max(na, nb);
        nOut[pairBase + 1] = Math.Min(na, nb);

        nuint nua = (nuint)ulA[t];
        nuint nub = (nuint)ulB[t];
        nuOut[pairBase + 0] = Math.Max(nua, nub);
        nuOut[pairBase + 1] = Math.Min(nua, nub);
    }

    [GpuKernel]
    public static void NarrowClampKernel(
        sbyte[] sbV,
        sbyte[] sbLo,
        sbyte[] sbHi,
        byte[] bV,
        byte[] bLo,
        byte[] bHi,
        short[] sV,
        short[] sLo,
        short[] sHi,
        ushort[] usV,
        ushort[] usLo,
        ushort[] usHi,
        sbyte[] sbOut,
        byte[] bOut,
        short[] sOut,
        ushort[] usOut,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        sbOut[t] = Math.Clamp(sbV[t], sbLo[t], sbHi[t]);
        bOut[t] = Math.Clamp(bV[t], bLo[t], bHi[t]);
        sOut[t] = Math.Clamp(sV[t], sLo[t], sHi[t]);
        usOut[t] = Math.Clamp(usV[t], usLo[t], usHi[t]);
    }

    [GpuKernel]
    public static void WideClampKernel(
        uint[] uV,
        uint[] uLo,
        uint[] uHi,
        long[] lV,
        long[] lLo,
        long[] lHi,
        ulong[] ulV,
        ulong[] ulLo,
        ulong[] ulHi,
        uint[] uOut,
        long[] lOut,
        ulong[] ulOut,
        long[] nOut,
        ulong[] nuOut,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        uOut[t] = Math.Clamp(uV[t], uLo[t], uHi[t]);
        lOut[t] = Math.Clamp(lV[t], lLo[t], lHi[t]);
        ulOut[t] = Math.Clamp(ulV[t], ulLo[t], ulHi[t]);
        nOut[t] = Math.Clamp((nint)lV[t], (nint)lLo[t], (nint)lHi[t]);
        nuOut[t] = Math.Clamp((nuint)ulV[t], (nuint)ulLo[t], (nuint)ulHi[t]);
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

    [GpuDeviceFunction]
    public static void IncrementByte(ref byte x) => x++;

    [GpuDeviceFunction]
    public static void IncrementSByte(ref sbyte x) => x++;

    [GpuDeviceFunction]
    public static void IncrementInt16(ref short x) => x++;

    [GpuDeviceFunction]
    public static void IncrementUInt16(ref ushort x) => x++;

    // Each call is bound by C# to one overload; the comment names it. The narrow results kept in `var` and passed by
    // ref must keep their type: a pointer to an int would not convert to a pointer to the narrow type
    [GpuKernel]
    public static void MixedOperandKernel(
        long[] l,
        byte[] b,
        ushort[] us,
        sbyte[] sb,
        uint[] u,
        int[] iv,
        short[] s,
        long[] r,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int o = t * 22;
        r[o] = Math.Max(l[t], 0); // Max(long, long)
        r[o + 1] = Math.Clamp(l[t], -5, 5); // Clamp(long, long, long)
        r[o + 2] = Math.Max(us[t], b[t]); // Max(ushort, ushort)
        r[o + 3] = Math.Max(b[t], sb[t]); // Max(short, short)
        r[o + 4] = Math.Max(u[t], 0); // Max(uint, uint)
        r[o + 5] = Math.Max(u[t], iv[t]); // Max(long, long)
        r[o + 6] = Math.Min((nint)l[t], 3); // Min(nint, nint)
        r[o + 7] = Math.Max((nint)l[t], iv[t]); // Max(nint, nint)
        r[o + 8] = Math.Clamp(b[t], (byte)10, (byte)200);
        var mb = Math.Max(b[t], (byte)254);
        mb++;
        r[o + 9] = mb;
        var ms = Math.Max(sb[t], (sbyte)0);
        ms++;
        r[o + 10] = ms;
        var cu = Math.Clamp(us[t], (ushort)0, (ushort)65535);
        cu++;
        r[o + 11] = cu;
        var mn = Math.Min(s[t], (short)32767);
        mn++;
        r[o + 12] = mn;
        r[o + 13] = BitOperations.IsPow2(b[t]) ? 1 : 0; // IsPow2(int)
        r[o + 14] = BitOperations.TrailingZeroCount(sb[t]); // TrailingZeroCount(int)
        r[o + 15] = BitOperations.RoundUpToPowerOf2(us[t]); // RoundUpToPowerOf2(uint)
        r[o + 16] = Math.Sign(b[t]); // Sign(short)
        r[o + 17] = Math.Abs(b[t]); // Abs(short)
        var rb = Math.Max(b[t], (byte)254);
        IncrementByte(ref rb);
        r[o + 18] = rb;
        var rs = Math.Min(s[t], (short)32767);
        IncrementInt16(ref rs);
        r[o + 19] = rs;
        var rsb = Math.Max(sb[t], (sbyte)0);
        IncrementSByte(ref rsb);
        r[o + 20] = rsb;
        var rus = Math.Clamp(us[t], (ushort)0, (ushort)65535);
        IncrementUInt16(ref rus);
        r[o + 21] = rus;
    }
}
