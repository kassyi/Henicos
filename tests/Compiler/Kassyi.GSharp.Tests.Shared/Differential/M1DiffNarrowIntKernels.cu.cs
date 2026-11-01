// ReSharper disable All
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for narrow integers, narrow/signed enums, mixed-sign remainder/division, bit-operation
/// return signedness, and compound/tuple assignment target evaluation order.
/// </summary>
public static partial class M1DiffNarrowIntKernels
{
    /// <summary>The columns of <see cref="NarrowCompoundAndStepMatrixKernel"/>.</summary>
    public static readonly string[] NarrowCompoundAndStepColumns =
    [
        "b+=b2",
        "sb-=sb2",
        "s+=s2",
        "s*=s2",
        "us+=us2",
        "us*=us2",
        "c+=c2",
        "s<<=sh",
        "s>>=sh",
        "s>>>=sh",
        "sb>>>=sh",
        "b>>>=sh",
        "us>>>=sh",
        "s>>>sh",
        "sb>>>sh",
        "b>>>sh",
        "us>>>sh",
        "c>>>sh",
        "~s",
        "~b",
        "~c",
        "-s",
        "-sb",
        "++sb",
        "sb++",
        "--sb",
        "++b",
        "--b",
        "++s",
        "--s",
        "++us",
        "--us",
        "++c",
        "--c",
    ];

    /// <summary>The columns of <see cref="NarrowEnumAndSignedEnumMatrixKernel"/>.</summary>
    public static readonly string[] NarrowEnumAndSignedEnumColumns =
    [
        "(int)~byteEnum",
        "(int)~ushortEnum",
        "(int)~sbyteEnum",
        "(int)~shortEnum",
        "~ByteEnum.Zero>ByteEnum.Zero",
        "(int)(byteEnum+b)",
        "(int)(byteEnum-b)",
        "(int)(byteEnum-byteEnum2)",
        "(int)(ushortEnum+us)",
        "(int)(ushortEnum-ushortEnum2)",
        "(int)(shortEnum+s)",
        "(int)(sbyteEnum-sb)",
        "(int)(intEnum+1)",
        "(int)(intEnum-1)",
        "intEnum+1<intEnum",
        "intEnum-1>intEnum",
        "intEnum+=1",
        "intEnum++",
    ];

    /// <summary>The columns of <see cref="MixedSignDivRemMatrixKernel"/>.</summary>
    public static readonly string[] MixedSignDivRemColumns =
    [
        "long%int",
        "int%long",
        "long/int",
        "int/long",
        "uint+long",
        "uint-long",
        "uint*long",
        "uint/long",
        "uint%long",
        "long/uint",
        "long%uint",
        "short%int",
        "sbyte%short",
        "ushort/sbyte",
    ];

    /// <summary>The columns of <see cref="BitOpsReturnSignMatrixKernel"/>.</summary>
    public static readonly string[] BitOpsReturnSignColumns =
    [
        "PopCount(u)<-1",
        "LeadingZeroCount(u)<-1",
        "TrailingZeroCount(u)<-1",
        "Log2(u)<-1",
        "PopCount(ul)<-1",
        "LeadingZeroCount(ul)<-1",
        "TrailingZeroCount(ul)<-1",
        "Log2(ul)<-1",
        "PopCount(u)/-1",
        "LeadingZeroCount(ul)/-1",
    ];

    /// <summary>The columns of <see cref="TargetEvaluationOrderMatrixKernel"/>.</summary>
    public static readonly string[] TargetEvaluationOrderColumns =
    [
        "(i==0?s:alt)[++i]+=v s[1]",
        "(i==0?s:alt)[++i]+=v alt[1]",
        "(i==0?s:alt)[++i]++ s[1]",
        "(i==0?s:alt)[++i]++ alt[1]",
        "(i,s[i])=(1,v) [0]",
        "(i,s[i])=(1,v) [1]",
    ];

    // Narrow integer compound assignments, shifts, >>>, unary ~/unary -, and boundary ++/--
    [GpuKernel]
    public static void NarrowCompoundAndStepMatrixKernel(int[] a, int[] b, int[] sh, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 34;
        int av = a[t];
        int bv = b[t];
        int sCount = sh[t];

        byte b1 = (byte)av;
        byte b2 = (byte)bv;
        b1 += b2;
        r[o] = b1;

        sbyte sb1 = (sbyte)av;
        sbyte sb2 = (sbyte)bv;
        sb1 -= sb2;
        r[o + 1] = sb1;

        short s1 = (short)av;
        short s2 = (short)bv;
        s1 += s2;
        r[o + 2] = s1;

        short sMul = (short)av;
        sMul *= s2;
        r[o + 3] = sMul;

        ushort us1 = (ushort)av;
        ushort us2 = (ushort)bv;
        us1 += us2;
        r[o + 4] = us1;

        ushort usMul = (ushort)av;
        usMul *= us2;
        r[o + 5] = usMul;

        char c1 = (char)av;
        char c2 = (char)bv;
        c1 += c2;
        r[o + 6] = c1;

        short sShl = (short)av;
        sShl <<= sCount;
        r[o + 7] = sShl;

        short sShr = (short)av;
        sShr >>= sCount;
        r[o + 8] = sShr;

        short sUshr = (short)av;
        sUshr >>>= sCount;
        r[o + 9] = sUshr;

        sbyte sbUshr = (sbyte)av;
        sbUshr >>>= sCount;
        r[o + 10] = sbUshr;

        byte bUshr = (byte)av;
        bUshr >>>= sCount;
        r[o + 11] = bUshr;

        ushort usUshr = (ushort)av;
        usUshr >>>= sCount;
        r[o + 12] = usUshr;

        short sVal = (short)av;
        sbyte sbVal = (sbyte)av;
        byte bVal = (byte)av;
        ushort usVal = (ushort)av;
        char cVal = (char)av;

        r[o + 13] = sVal >>> sCount;
        r[o + 14] = sbVal >>> sCount;
        r[o + 15] = bVal >>> sCount;
        r[o + 16] = usVal >>> sCount;
        r[o + 17] = cVal >>> sCount;

        r[o + 18] = ~sVal;
        r[o + 19] = ~bVal;
        r[o + 20] = ~cVal;
        r[o + 21] = -sVal;
        r[o + 22] = -sbVal;

        sbyte sbPreInc = sbVal;
        r[o + 23] = ++sbPreInc;

        sbyte sbPostInc = sbVal;
        int postBefore = sbPostInc++;
        r[o + 24] = postBefore + sbPostInc * 1000;

        sbyte sbPreDec = sbVal;
        r[o + 25] = --sbPreDec;

        byte bPreInc = bVal;
        r[o + 26] = ++bPreInc;

        byte bPreDec = bVal;
        r[o + 27] = --bPreDec;

        short sPreInc = sVal;
        r[o + 28] = ++sPreInc;

        short sPreDec = sVal;
        r[o + 29] = --sPreDec;

        ushort usPreInc = usVal;
        r[o + 30] = ++usPreInc;

        ushort usPreDec = usVal;
        r[o + 31] = --usPreDec;

        char cPreInc = cVal;
        r[o + 32] = ++cPreInc;

        char cPreDec = cVal;
        r[o + 33] = --cPreDec;
    }

    // C# §12.10.4-12.10.6: ~e, e + u, e - u have type E (narrowed to the underlying width); e1 - e2 has type U
    [GpuKernel]
    public static void NarrowEnumAndSignedEnumMatrixKernel(int[] a, int[] b, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 18;
        int av = a[t];
        int bv = b[t];

        M1DiffByteEnum be = (M1DiffByteEnum)(byte)av;
        M1DiffByteEnum be2 = (M1DiffByteEnum)(byte)bv;
        M1DiffUShortEnum use = (M1DiffUShortEnum)(ushort)av;
        M1DiffUShortEnum use2 = (M1DiffUShortEnum)(ushort)bv;
        M1DiffSByteEnum sbe = (M1DiffSByteEnum)(sbyte)av;
        M1DiffShortEnum se = (M1DiffShortEnum)(short)av;
        M1DiffIntEnum ie = (M1DiffIntEnum)av;

        byte b2 = (byte)bv;
        ushort us2 = (ushort)bv;
        sbyte sb2 = (sbyte)bv;
        short s2 = (short)bv;

        r[o] = (int)(~be);
        r[o + 1] = (int)(~use);
        r[o + 2] = (int)(~sbe);
        r[o + 3] = (int)(~se);
        r[o + 4] = (~M1DiffByteEnum.Zero) > M1DiffByteEnum.Zero ? 1 : 0;
        r[o + 5] = (int)(be + b2);
        r[o + 6] = (int)(be - b2);
        r[o + 7] = (int)(be - be2);
        r[o + 8] = (int)(use + us2);
        r[o + 9] = (int)(use - use2);
        r[o + 10] = (int)(se + s2);
        r[o + 11] = (int)(sbe - sb2);
        r[o + 12] = (int)(ie + 1);
        r[o + 13] = (int)(ie - 1);
        r[o + 14] = (ie + 1) < ie ? 1 : 0;
        r[o + 15] = (ie - 1) > ie ? 1 : 0;

        M1DiffIntEnum ieAdd = ie;
        ieAdd += 1;
        r[o + 16] = (int)ieAdd;

        M1DiffIntEnum ieInc = ie;
        ieInc++;
        r[o + 17] = (int)ieInc;
    }

    // Mixed-sign and mixed-width division, remainder, and arithmetic (with safe non-zero divisors)
    [GpuKernel]
    public static void MixedSignDivRemMatrixKernel(long[] l, int[] iNz, uint[] uNz, long[] lNz, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= l.Length)
        {
            return;
        }

        int o = t * 14;
        long lv = l[t];
        int iv = iNz[t];
        uint uv = uNz[t];
        long lDiv = lNz[t];

        short sv = (short)lv;
        sbyte sbv = (sbyte)lv;
        ushort usv = (ushort)lv;
        short sDiv = (short)iv;
        if (sDiv == 0 || sDiv == -1)
        {
            sDiv = 3;
        }

        sbyte sbDiv = (sbyte)iv;
        if (sbDiv == 0 || sbDiv == -1)
        {
            sbDiv = 3;
        }

        r[o] = lv % iv;
        r[o + 1] = iv % lDiv;
        r[o + 2] = lv / iv;
        r[o + 3] = iv / lDiv;
        r[o + 4] = uv + lv;
        r[o + 5] = uv - lv;
        r[o + 6] = uv * lv;
        r[o + 7] = uv / lDiv;
        r[o + 8] = uv % lDiv;
        r[o + 9] = lv / uv;
        r[o + 10] = lv % uv;
        r[o + 11] = sv % iv;
        r[o + 12] = sbv % sDiv;
        r[o + 13] = usv / sbDiv;
    }

    // BitOperations return signedness: C# returns int, CPU prelude returns uint32_t / uint64_t (#876)
    [GpuKernel]
    public static void BitOpsReturnSignMatrixKernel(uint[] u, ulong[] ul, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= u.Length)
        {
            return;
        }

        int o = t * 10;
        uint uv = u[t];
        ulong ulv = ul[t];

        r[o] = BitOperations.PopCount(uv) < -1 ? 1 : 0;
        r[o + 1] = BitOperations.LeadingZeroCount(uv) < -1 ? 1 : 0;
        r[o + 2] = BitOperations.TrailingZeroCount(uv) < -1 ? 1 : 0;
        r[o + 3] = BitOperations.Log2(uv) < -1 ? 1 : 0;
        r[o + 4] = BitOperations.PopCount(ulv) < -1 ? 1 : 0;
        r[o + 5] = BitOperations.LeadingZeroCount(ulv) < -1 ? 1 : 0;
        r[o + 6] = BitOperations.TrailingZeroCount(ulv) < -1 ? 1 : 0;
        r[o + 7] = BitOperations.Log2(ulv) < -1 ? 1 : 0;
        r[o + 8] = BitOperations.PopCount(uv) / -1;
        r[o + 9] = BitOperations.LeadingZeroCount(ulv) / -1;
    }

    // Left-to-right target evaluation order on compound assignment, ++, and deconstructing tuple assignment
    [GpuKernel]
    public static void TargetEvaluationOrderMatrixKernel(int[] a, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 6;
        int v = (a[t] & 0xFF) + 1;

        Span<int> s1 = stackalloc int[2];
        Span<int> sAlt1 = stackalloc int[2];
        s1[0] = 10;
        s1[1] = 20;
        sAlt1[0] = 100;
        sAlt1[1] = 200;
        int i1 = 0;
        (i1 == 0 ? s1 : sAlt1)[++i1] += v;
        r[o] = s1[1];
        r[o + 1] = sAlt1[1];

        Span<int> s2 = stackalloc int[2];
        Span<int> sAlt2 = stackalloc int[2];
        s2[0] = 10;
        s2[1] = 20;
        sAlt2[0] = 100;
        sAlt2[1] = 200;
        int i2 = 0;
        (i2 == 0 ? s2 : sAlt2)[++i2]++;
        r[o + 2] = s2[1];
        r[o + 3] = sAlt2[1];

        Span<int> s3 = stackalloc int[2];
        s3[0] = 10;
        s3[1] = 20;
        int i3 = 0;
        (i3, s3[i3]) = (1, v);
        r[o + 4] = s3[0];
        r[o + 5] = s3[1];
    }
}
