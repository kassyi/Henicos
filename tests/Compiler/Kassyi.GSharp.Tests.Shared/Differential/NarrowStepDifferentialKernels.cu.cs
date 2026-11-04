// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for prefix and postfix <c>++</c> and <c>--</c> on narrow integer types
/// (<see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>, <see cref="char"/>) (#1079).
/// </summary>
public static partial class NarrowStepDifferentialKernels
{
    /// <summary>The columns of each narrow step kernel.</summary>
    public static readonly string[] NarrowStepColumns =
    [
        "cmp ++v",
        "cmp --v",
        "cmp v++",
        "cmp v--",
        "expr ++v",
        "expr --v",
        "expr v++",
        "expr v--",
        "stmt ++v",
        "stmt --v",
        "stmt v++",
        "stmt v--",
    ];

    /// <summary>12 forms of prefix and postfix step on <see cref="sbyte"/> (#1079).</summary>
    [GpuKernel]
    public static void SByteStepKernel(int[] a, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 12;
        int av = a[t];

        sbyte sb0 = (sbyte)av;
        bool sbCmpPreInc = ((sbyte)(sb0 - sb0)) >= (++sb0);
        r[o] = (sbCmpPreInc ? 1L : 2L) * 100000L + (ushort)sb0;

        sbyte sb1 = (sbyte)av;
        bool sbCmpPreDec = ((sbyte)(sb1 - sb1)) <= (--sb1);
        r[o + 1] = (sbCmpPreDec ? 1L : 2L) * 100000L + (ushort)sb1;

        sbyte sb2 = (sbyte)av;
        bool sbCmpPostInc = ((sbyte)(sb2 - sb2)) >= (sb2++);
        r[o + 2] = (sbCmpPostInc ? 1L : 2L) * 100000L + (ushort)sb2;

        sbyte sb3 = (sbyte)av;
        bool sbCmpPostDec = ((sbyte)(sb3 - sb3)) <= (sb3--);
        r[o + 3] = (sbCmpPostDec ? 1L : 2L) * 100000L + (ushort)sb3;

        sbyte sb4 = (sbyte)av;
        long sbExprPreInc = ((sbyte)(sb4 - sb4)) + (++sb4);
        r[o + 4] = sbExprPreInc * 100000L + (ushort)sb4;

        sbyte sb5 = (sbyte)av;
        long sbExprPreDec = ((sbyte)(sb5 - sb5)) + (--sb5);
        r[o + 5] = sbExprPreDec * 100000L + (ushort)sb5;

        sbyte sb6 = (sbyte)av;
        long sbExprPostInc = ((sbyte)(sb6 - sb6)) + (sb6++);
        r[o + 6] = sbExprPostInc * 100000L + (ushort)sb6;

        sbyte sb7 = (sbyte)av;
        long sbExprPostDec = ((sbyte)(sb7 - sb7)) + (sb7--);
        r[o + 7] = sbExprPostDec * 100000L + (ushort)sb7;

        sbyte sb8 = (sbyte)av;
        ++sb8;
        r[o + 8] = sb8;

        sbyte sb9 = (sbyte)av;
        --sb9;
        r[o + 9] = sb9;

        sbyte sb10 = (sbyte)av;
        sb10++;
        r[o + 10] = sb10;

        sbyte sb11 = (sbyte)av;
        sb11--;
        r[o + 11] = sb11;
    }

    /// <summary>12 forms of prefix and postfix step on <see cref="byte"/> (#1079).</summary>
    [GpuKernel]
    public static void ByteStepKernel(int[] a, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 12;
        int av = a[t];

        byte b0 = (byte)av;
        bool bCmpPreInc = ((byte)(b0 - b0)) >= (++b0);
        r[o] = (bCmpPreInc ? 1L : 2L) * 100000L + b0;

        byte b1 = (byte)av;
        bool bCmpPreDec = ((byte)(b1 - b1)) <= (--b1);
        r[o + 1] = (bCmpPreDec ? 1L : 2L) * 100000L + b1;

        byte b2 = (byte)av;
        bool bCmpPostInc = ((byte)(b2 - b2)) >= (b2++);
        r[o + 2] = (bCmpPostInc ? 1L : 2L) * 100000L + b2;

        byte b3 = (byte)av;
        bool bCmpPostDec = ((byte)(b3 - b3)) <= (b3--);
        r[o + 3] = (bCmpPostDec ? 1L : 2L) * 100000L + b3;

        byte b4 = (byte)av;
        long bExprPreInc = ((byte)(b4 - b4)) + (++b4);
        r[o + 4] = bExprPreInc * 100000L + b4;

        byte b5 = (byte)av;
        long bExprPreDec = ((byte)(b5 - b5)) + (--b5);
        r[o + 5] = bExprPreDec * 100000L + b5;

        byte b6 = (byte)av;
        long bExprPostInc = ((byte)(b6 - b6)) + (b6++);
        r[o + 6] = bExprPostInc * 100000L + b6;

        byte b7 = (byte)av;
        long bExprPostDec = ((byte)(b7 - b7)) + (b7--);
        r[o + 7] = bExprPostDec * 100000L + b7;

        byte b8 = (byte)av;
        ++b8;
        r[o + 8] = b8;

        byte b9 = (byte)av;
        --b9;
        r[o + 9] = b9;

        byte b10 = (byte)av;
        b10++;
        r[o + 10] = b10;

        byte b11 = (byte)av;
        b11--;
        r[o + 11] = b11;
    }

    /// <summary>12 forms of prefix and postfix step on <see cref="short"/> (#1079).</summary>
    [GpuKernel]
    public static void Int16StepKernel(int[] a, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 12;
        int av = a[t];

        short s0 = (short)av;
        bool sCmpPreInc = ((short)(s0 - s0)) >= (++s0);
        r[o] = (sCmpPreInc ? 1L : 2L) * 100000L + (ushort)s0;

        short s1 = (short)av;
        bool sCmpPreDec = ((short)(s1 - s1)) <= (--s1);
        r[o + 1] = (sCmpPreDec ? 1L : 2L) * 100000L + (ushort)s1;

        short s2 = (short)av;
        bool sCmpPostInc = ((short)(s2 - s2)) >= (s2++);
        r[o + 2] = (sCmpPostInc ? 1L : 2L) * 100000L + (ushort)s2;

        short s3 = (short)av;
        bool sCmpPostDec = ((short)(s3 - s3)) <= (s3--);
        r[o + 3] = (sCmpPostDec ? 1L : 2L) * 100000L + (ushort)s3;

        short s4 = (short)av;
        long sExprPreInc = ((short)(s4 - s4)) + (++s4);
        r[o + 4] = sExprPreInc * 100000L + (ushort)s4;

        short s5 = (short)av;
        long sExprPreDec = ((short)(s5 - s5)) + (--s5);
        r[o + 5] = sExprPreDec * 100000L + (ushort)s5;

        short s6 = (short)av;
        long sExprPostInc = ((short)(s6 - s6)) + (s6++);
        r[o + 6] = sExprPostInc * 100000L + (ushort)s6;

        short s7 = (short)av;
        long sExprPostDec = ((short)(s7 - s7)) + (s7--);
        r[o + 7] = sExprPostDec * 100000L + (ushort)s7;

        short s8 = (short)av;
        ++s8;
        r[o + 8] = s8;

        short s9 = (short)av;
        --s9;
        r[o + 9] = s9;

        short s10 = (short)av;
        s10++;
        r[o + 10] = s10;

        short s11 = (short)av;
        s11--;
        r[o + 11] = s11;
    }

    /// <summary>12 forms of prefix and postfix step on <see cref="ushort"/> (#1079).</summary>
    [GpuKernel]
    public static void UInt16StepKernel(int[] a, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 12;
        int av = a[t];

        ushort us0 = (ushort)av;
        bool usCmpPreInc = ((ushort)(us0 - us0)) >= (++us0);
        r[o] = (usCmpPreInc ? 1L : 2L) * 100000L + us0;

        ushort us1 = (ushort)av;
        bool usCmpPreDec = ((ushort)(us1 - us1)) <= (--us1);
        r[o + 1] = (usCmpPreDec ? 1L : 2L) * 100000L + us1;

        ushort us2 = (ushort)av;
        bool usCmpPostInc = ((ushort)(us2 - us2)) >= (us2++);
        r[o + 2] = (usCmpPostInc ? 1L : 2L) * 100000L + us2;

        ushort us3 = (ushort)av;
        bool usCmpPostDec = ((ushort)(us3 - us3)) <= (us3--);
        r[o + 3] = (usCmpPostDec ? 1L : 2L) * 100000L + us3;

        ushort us4 = (ushort)av;
        long usExprPreInc = ((ushort)(us4 - us4)) + (++us4);
        r[o + 4] = usExprPreInc * 100000L + us4;

        ushort us5 = (ushort)av;
        long usExprPreDec = ((ushort)(us5 - us5)) + (--us5);
        r[o + 5] = usExprPreDec * 100000L + us5;

        ushort us6 = (ushort)av;
        long usExprPostInc = ((ushort)(us6 - us6)) + (us6++);
        r[o + 6] = usExprPostInc * 100000L + us6;

        ushort us7 = (ushort)av;
        long usExprPostDec = ((ushort)(us7 - us7)) + (us7--);
        r[o + 7] = usExprPostDec * 100000L + us7;

        ushort us8 = (ushort)av;
        ++us8;
        r[o + 8] = us8;

        ushort us9 = (ushort)av;
        --us9;
        r[o + 9] = us9;

        ushort us10 = (ushort)av;
        us10++;
        r[o + 10] = us10;

        ushort us11 = (ushort)av;
        us11--;
        r[o + 11] = us11;
    }

    /// <summary>12 forms of prefix and postfix step on <see cref="char"/> (#1079).</summary>
    [GpuKernel]
    public static void CharStepKernel(int[] a, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 12;
        int av = a[t];

        char c0 = (char)av;
        bool cCmpPreInc = ((char)(c0 - c0)) >= (++c0);
        r[o] = (cCmpPreInc ? 1L : 2L) * 100000L + c0;

        char c1 = (char)av;
        bool cCmpPreDec = ((char)(c1 - c1)) <= (--c1);
        r[o + 1] = (cCmpPreDec ? 1L : 2L) * 100000L + c1;

        char c2 = (char)av;
        bool cCmpPostInc = ((char)(c2 - c2)) >= (c2++);
        r[o + 2] = (cCmpPostInc ? 1L : 2L) * 100000L + c2;

        char c3 = (char)av;
        bool cCmpPostDec = ((char)(c3 - c3)) <= (c3--);
        r[o + 3] = (cCmpPostDec ? 1L : 2L) * 100000L + c3;

        char c4 = (char)av;
        long cExprPreInc = ((char)(c4 - c4)) + (++c4);
        r[o + 4] = cExprPreInc * 100000L + c4;

        char c5 = (char)av;
        long cExprPreDec = ((char)(c5 - c5)) + (--c5);
        r[o + 5] = cExprPreDec * 100000L + c5;

        char c6 = (char)av;
        long cExprPostInc = ((char)(c6 - c6)) + (c6++);
        r[o + 6] = cExprPostInc * 100000L + c6;

        char c7 = (char)av;
        long cExprPostDec = ((char)(c7 - c7)) + (c7--);
        r[o + 7] = cExprPostDec * 100000L + c7;

        char c8 = (char)av;
        ++c8;
        r[o + 8] = c8;

        char c9 = (char)av;
        --c9;
        r[o + 9] = c9;

        char c10 = (char)av;
        c10++;
        r[o + 10] = c10;

        char c11 = (char)av;
        c11--;
        r[o + 11] = c11;
    }
}
