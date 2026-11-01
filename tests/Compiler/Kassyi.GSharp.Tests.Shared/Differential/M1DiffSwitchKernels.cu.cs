using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public enum M1DiffSwitchMode : int
{
    Alpha = -5,
    Zero = 0,
    Beta = 7,
    Gamma = 42,
}

public enum M1DiffSwitchByteTag : byte
{
    Low = 0,
    Mid = 128,
    High = 255,
}

/// <summary>
/// Differential kernels (<c>switch</c> statements, <c>switch</c> expressions,
/// pattern matching <c>is null</c> / <c>is not null</c>, ternary <c>?:</c>, null-coalescing <c>??</c> / <c>??=</c>,
/// and <c>goto</c> / <c>goto case</c> / <c>goto default</c>).
/// </summary>
public static partial class M1DiffSwitchKernels
{
    public const int ConstNegCase = -17;
    public const int ConstPosCase = 23;
    public const long ConstLongMin = long.MinValue;
    public const long ConstLongMax = long.MaxValue;

    // -------------------------------------------------------------------------
    // 1. Switch statements: int, negative cases, multiple labels, no-default fall-out, nested switch
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchIntStatementKernel(int[] a, int[] b, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int w = b[t];
        int col = t * 4;

        // Col 0: standard switch with negative, zero, positive, int.MinValue, int.MaxValue, multi-label section, default
        int r0;
        switch (v)
        {
            case int.MinValue:
                r0 = -1000;
                break;
            case ConstNegCase:
            case -1:
                r0 = v * 3 - 1;
                break;
            case 0:
                r0 = 100;
                break;
            case 1:
            case 2:
            case 3:
                r0 = v * 10 + (w & 0xFFFF);
                break;
            case ConstPosCase:
                r0 = 777;
                break;
            case int.MaxValue:
                r0 = 1000;
                break;
            default:
                r0 = (v ^ w) & 0x7FFF;
                break;
        }
        dst[col + 0] = r0;

        // Col 1: switch with NO default section (prior value preserved when no case matches)
        int r1 = 5555;
        switch (v & 7)
        {
            case 0:
                r1 = 10;
                break;
            case 3:
            case 5:
                r1 = 35 + (w & 0xFF);
                break;
            case 7:
                r1 = -70;
                break;
        }
        dst[col + 1] = r1;

        // Col 2: nested switch statements with independent break targets
        int r2 = 0;
        switch (v & 3)
        {
            case 0:
                switch (w & 3)
                {
                    case 0:
                        r2 = 11;
                        break;
                    case 1:
                    case 2:
                        r2 = 12;
                        break;
                    default:
                        r2 = 13;
                        break;
                }
                r2 += 100;
                break;
            case 1:
            case 2:
                switch (w & 1)
                {
                    case 0:
                        r2 = 20 + (v & 3);
                        break;
                    default:
                        r2 = 30 + (v & 3);
                        break;
                }
                break;
            default:
                r2 = -99;
                break;
        }
        dst[col + 2] = r2;

        // Col 3: bool switch statement
        int r3;
        bool flag = (v & 1) == 0;
        switch (flag)
        {
            case true:
                r3 = (w & 0xFF) + 1;
                break;
            case false:
                r3 = -((w & 0xFF) + 1);
                break;
        }
        dst[col + 3] = r3;
    }

    // -------------------------------------------------------------------------
    // 2. Switch statements: 64-bit long and ulong governing types (including > 32-bit constants)
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchWideInt64StatementKernel(long[] a, ulong[] ua, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        long lv = a[t];
        ulong ulv = ua[t];
        int col = t * 2;

        long r0;
        switch (lv)
        {
            case ConstLongMin:
                r0 = -9000000000000L;
                break;
            case -5000000000L:
                r0 = -500L;
                break;
            case -1L:
            case 0L:
                r0 = lv + 42L;
                break;
            case 5000000000L:
                r0 = 500L;
                break;
            case ConstLongMax:
                r0 = 9000000000000L;
                break;
            default:
                r0 = (lv >> 16) ^ 0x1234L;
                break;
        }
        dst[col + 0] = r0;

        long r1;
        switch (ulv)
        {
            case 0UL:
                r1 = 10L;
                break;
            case 1UL:
            case 2UL:
                r1 = (long)ulv + 100L;
                break;
            case 0x1_0000_0000UL:
                r1 = 4294967296L;
                break;
            case 0x8000_0000_0000_0000UL:
                r1 = -123456L;
                break;
            case ulong.MaxValue:
                r1 = 777777L;
                break;
            default:
                r1 = (long)(ulv & 0xFFFF_FFFFUL);
                break;
        }
        dst[col + 1] = r1;
    }

    // -------------------------------------------------------------------------
    // 3. Switch statements: narrow integer types (byte, sbyte, short, ushort), char, and enum
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchNarrowCharEnumStatementKernel(
        byte[] ba,
        sbyte[] sba,
        short[] sa,
        ushort[] usa,
        int[] dst,
        int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        byte bv = ba[t];
        sbyte sbv = sba[t];
        short sv = sa[t];
        ushort usv = usa[t];
        int col = t * 4;

        // Col 0: byte and sbyte switches
        int r0;
        switch (bv)
        {
            case 0:
                r0 = 10;
                break;
            case 127:
            case 128:
                r0 = 20 + bv;
                break;
            case byte.MaxValue:
                r0 = 999;
                break;
            default:
                r0 = bv;
                break;
        }
        switch (sbv)
        {
            case sbyte.MinValue:
                r0 += -1000;
                break;
            case -1:
            case 0:
                r0 += sbv * 10;
                break;
            case sbyte.MaxValue:
                r0 += 1000;
                break;
            default:
                r0 += sbv;
                break;
        }
        dst[col + 0] = r0;

        // Col 1: short and ushort switches
        int r1;
        switch (sv)
        {
            case short.MinValue:
                r1 = -30000;
                break;
            case -1:
            case 0:
            case 1:
                r1 = sv * 100;
                break;
            case short.MaxValue:
                r1 = 30000;
                break;
            default:
                r1 = sv;
                break;
        }
        switch (usv)
        {
            case 0:
                r1 += 1;
                break;
            case 32768:
                r1 += 2;
                break;
            case ushort.MaxValue:
                r1 += 3;
                break;
            default:
                r1 += (usv & 0xFF);
                break;
        }
        dst[col + 1] = r1;

        // Col 2: char switch (ASCII and non-ASCII UTF-16 code units)
        char ch = (t & 3) switch
        {
            0 => 'A',
            1 => '\0',
            2 => '\u03B1',
            _ => 'Z'
        };
        int r2;
        switch (ch)
        {
            case '\0':
                r2 = 0;
                break;
            case 'A':
                r2 = 65 + (bv & 0xF);
                break;
            case '\u03B1':
                r2 = 945;
                break;
            default:
                r2 = ch;
                break;
        }
        dst[col + 2] = r2;

        // Col 3: enum switch (signed int-backed and byte-backed enums)
        M1DiffSwitchMode mode = (t % 4) switch
        {
            0 => M1DiffSwitchMode.Alpha,
            1 => M1DiffSwitchMode.Zero,
            2 => M1DiffSwitchMode.Beta,
            _ => M1DiffSwitchMode.Gamma
        };
        int r3;
        switch (mode)
        {
            case M1DiffSwitchMode.Alpha:
                r3 = -50;
                break;
            case M1DiffSwitchMode.Zero:
                r3 = 0;
                break;
            case M1DiffSwitchMode.Beta:
                r3 = 70;
                break;
            default:
                r3 = 420;
                break;
        }
        M1DiffSwitchByteTag btag = bv switch
        {
            0 => M1DiffSwitchByteTag.Low,
            255 => M1DiffSwitchByteTag.High,
            _ => M1DiffSwitchByteTag.Mid
        };
        switch (btag)
        {
            case M1DiffSwitchByteTag.Low:
                r3 += 1;
                break;
            case M1DiffSwitchByteTag.High:
                r3 += 9;
                break;
            default:
                r3 += 5;
                break;
        }
        dst[col + 3] = r3;
    }

    // -------------------------------------------------------------------------
    // 4. Switch statements: goto case <const>, goto default, shared section locals/consts, user labels
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchGotoAndSharedLocalsKernel(int[] a, int[] b, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int w = b[t];
        int col = t * 4;

        // Col 0: goto case with positive, negative, const field, and goto default chains
        int acc = 0;
        switch (v % 5)
        {
            case -4:
            case -3:
                acc += 100;
                goto case ConstNegCase;
            case ConstNegCase:
            case -2:
            case -1:
                acc += 20;
                goto case 0;
            case 0:
                acc += 3;
                break;
            case 1:
                acc += 500;
                goto default;
            case 2:
                acc += 2000;
                goto case 0;
            default:
                acc += 70;
                break;
        }
        dst[col + 0] = acc;

        // Col 1: shared non-const local and shared const across switch sections (#795)
        int r1 = 0;
        switch (v & 3)
        {
            case 0:
                const int BaseOffset = 10;
                const int ScaledOffset = BaseOffset * 3;
                int sharedLocal = (w & 0xFF) + ScaledOffset;
                r1 = sharedLocal;
                break;
            case 1:
                sharedLocal = (w & 0xFF) * 2 + BaseOffset;
                r1 = sharedLocal + ScaledOffset;
                break;
            case 2:
                r1 = 100;
                goto default;
            default:
                sharedLocal = BaseOffset + (w & 0xF);
                r1 += sharedLocal + ScaledOffset + 1;
                break;
        }
        dst[col + 1] = r1;

        // Col 2: goto case inside char switch
        char ch = (v & 3) switch
        {
            0 => 'a',
            1 => 'b',
            2 => 'c',
            _ => 'z'
        };
        int r2 = 0;
        switch (ch)
        {
            case 'a':
                r2 += 10;
                goto case 'b';
            case 'b':
                r2 += 20;
                break;
            case 'c':
                r2 += 30;
                goto default;
            default:
                r2 += 1;
                break;
        }
        dst[col + 2] = r2;

        // Col 3: forward and backward user goto jumping over initialized local declarations
        int r3 = 0;
        int steps = 0;
        if ((v & 1) != 0)
        {
            goto skipInit;
        }

        int jumpedLocal = (w & 0xFF) + 10;
        r3 += jumpedLocal;

    skipInit:
        jumpedLocal = 5;
        r3 += jumpedLocal;

    loopTop:
        steps++;
        r3 += steps;
        if (steps < 3)
        {
            goto loopTop;
        }

        dst[col + 3] = r3;
    }

    // -------------------------------------------------------------------------
    // 5. Switch expressions: int, long, ulong, nested switch expressions, side-effecting governing expr
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchExpressionIntAndLongKernel(int[] a, int[] b, long[] la, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int w = b[t];
        long lv = la[t];
        int col = t * 4;

        // Col 0: int switch expression with boundary constants
        int r0 = v switch
        {
            int.MinValue => -999,
            -1 => -10,
            0 => 0,
            1 => 10,
            int.MaxValue => 999,
            _ => (v & 0xFF) + (w & 0xFF)
        };
        dst[col + 0] = r0;

        // Col 1: nested switch expression
        int r1 = (v & 3) switch
        {
            0 => (w & 1) switch
            {
                0 => 100,
                _ => 101
            },
            1 => (w & 1) switch
            {
                0 => 200,
                _ => 201
            },
            2 => 300,
            _ => 400 + (w & 0xF)
        };
        dst[col + 1] = r1;

        // Col 2: 64-bit long switch expression
        long r2 = lv switch
        {
            long.MinValue => -10000000000L,
            -1L => -111L,
            0L => 222L,
            1L => 333L,
            long.MaxValue => 10000000000L,
            _ => (lv & 0xFFFFL) - 500L
        };
        dst[col + 2] = r2;

        // Col 3: side-effecting governing expression evaluated exactly once + EvaluationOrder interference
        int state = v & 0xFF;
        int matched = (state += 3) switch
        {
            3 => 1000,
            4 => 2000,
            5 => 3000,
            _ => 4000
        };
        // Also test binary left-to-right order when LHS is a switch expression that mutates state read by RHS
        int seq = ((state += 5) switch
        {
            8 => 10,
            9 => 20,
            _ => 30
        }) + state * 100;
        dst[col + 3] = matched + seq;
    }

    // -------------------------------------------------------------------------
    // 6. Switch expressions: float/double special values (+0.0f, -0.0f, NaN, +/-Infinity)
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchExpressionFloatSpecialKernel(int[] sel, float[] fa, float[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int s = sel[t];
        float fv = fa[t];
        int col = t * 2;

        // Col 0: switch expression returning IEEE-754 special float bit patterns (+0.0f vs -0.0f, NaN, +/-Infinity)
        dst[col + 0] = (s & 7) switch
        {
            0 => 0.0f,
            1 => -0.0f,
            2 => float.PositiveInfinity,
            3 => float.NegativeInfinity,
            4 => float.NaN,
            5 => -1.5f,
            6 => 42.25f,
            _ => fv
        };

        // Col 1: nested ternary with NaN comparisons (<, <=, >, >=, ==, !=)
        // In IEEE-754 / C#, any relational comparison (<, <=, >, >=, ==) with NaN is false, and != is true.
        float probe = (s & 3) == 0 ? float.NaN : fv;
        float probeOther = probe;
        int flags = 0;
        flags |= (probe < 0.0f ? 1 : 0);
        flags |= (probe >= 0.0f ? 2 : 0);
        flags |= (probe == probeOther ? 4 : 0);
        flags |= (probe != probeOther ? 8 : 0);
        flags |= (!(probe < 0.0f) ? 16 : 0);
        dst[col + 1] = flags;
    }

    // -------------------------------------------------------------------------
    // 7. Ternary (?:): nested associativity, short-circuit side effects, ref ternary
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void TernaryNestedAndRefKernel(int[] a, int[] b, int[] c, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int x = a[t];
        int y = b[t];
        int z = c[t];
        int col = t * 4;

        // Col 0: right-associative chain c1 ? v1 : c2 ? v2 : c3 ? v3 : v4
        int r0 = x > 0
            ? (y > 0 ? 11 : 12)
            : y > 0
                ? 21
                : z > 0
                    ? 31
                    : 41;
        dst[col + 0] = r0;

        // Col 1: only the selected branch is evaluated (unselected branch side effect does not run)
        int sideTrue = 10;
        int sideFalse = 20;
        int chosen = (x & 1) == 0
            ? (sideTrue += 5)
            : (sideFalse += 7);
        dst[col + 1] = chosen * 1000 + sideTrue * 10 + sideFalse;

        // Col 2: ref ternary as assignment target and compound assignment target
        int leftSlot = (x & 0xFF) + 1;
        int rightSlot = (y & 0xFF) + 2;
        ((z & 1) == 0 ? ref leftSlot : ref rightSlot) = 50;
        ((z & 2) == 0 ? ref leftSlot : ref rightSlot) += 7;
        dst[col + 2] = leftSlot * 1000 + rightSlot;

        // Col 3: ref ternary where RHS mutates variable read by LHS condition (C# evaluates LHS ref first)
        int p = (x & 1) == 0 ? 1 : -1;
        int slotA = 10;
        int slotB = 20;
        (p > 0 ? ref slotA : ref slotB) = (p = -1) + 100;
        dst[col + 3] = slotA * 1000 + slotB + p;
    }

    // -------------------------------------------------------------------------
    // 8. Ternary (?:): target-typed mixed conversions and narrow var preservation (#616, #671)
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void TernaryTargetTypedAndNarrowKernel(
        int[] conds,
        uint[] ua,
        int[] ia,
        short[] sa,
        ushort[] usa,
        byte[] ba,
        sbyte[] sba,
        long[] ldst,
        double[] ddst,
        int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int c = conds[t];
        uint uv = ua[t];
        int iv = ia[t];
        short sv = sa[t];
        ushort usv = usa[t];
        byte bv = ba[t];
        sbyte sbv = sba[t];
        int lcol = t * 4;
        int dcol = t * 2;

        // lcol 0: target-typed long = cond ? uint : int (and literal -1)
        long lr0 = (c & 1) != 0 ? uv : iv;
        long lr0Lit = (c & 2) != 0 ? uv : -1;
        ldst[lcol + 0] = lr0 + lr0Lit;

        // lcol 1: target-typed long = cond ? uint : short / sbyte
        long lr1 = ((c & 1) != 0 ? uv : sv) + ((c & 2) != 0 ? uv : sbv);
        ldst[lcol + 1] = lr1;

        // lcol 2: target-typed int = cond ? byte : sbyte, and cond ? short : ushort
        int ir2 = ((c & 1) != 0 ? bv : sbv) + ((c & 2) != 0 ? sv : usv);
        ldst[lcol + 2] = ir2;

        // lcol 3: narrow var preservation (var x = cond ? byte1 : byte2; x++; and var y = cond ? byte1 : 0; y++;)
        byte bMax = 255;
        var narrowByte = (c & 1) != 0 ? bMax : bv;
        narrowByte++;
        ushort usMax = 65535;
        var narrowUShort = (c & 2) != 0 ? usMax : usv;
        narrowUShort++;
        ldst[lcol + 3] = narrowByte + ((long)narrowUShort << 16);

        // dcol 0: target-typed double = cond ? uint : int (and literal -8)
        double dr0 = ((c & 1) != 0 ? uv : iv) + ((c & 2) != 0 ? uv : -8);
        ddst[dcol + 0] = dr0;

        // dcol 1: nested target-typed long converted to double: c1 ? uv : (c2 ? iv : -1)
        long nestedLong = (c & 1) != 0 ? uv : ((c & 2) != 0 ? iv : -1);
        ddst[dcol + 1] = nestedLong;
    }

    // -------------------------------------------------------------------------
    // 9. Null-coalescing (??, ??=) and null pattern / equality checks that match
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NullCoalesceAndNullCheckMatchingKernel(int[] a, int[] b, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int w = b[t];
        int col = t * 4;

        // Col 0: local int? with ?? and short-circuiting of RHS side effects when non-null
        int? maybeVal = (v & 1) == 0 ? new int?(v & 0xFFFF) : new int?();
        int rhsSideEffect = 10;
        int coalesced = maybeVal ?? (rhsSideEffect += 5);
        dst[col + 0] = coalesced + rhsSideEffect * 100;

        // Col 1: chained int? ?? int? ?? int
        int? first = (v & 3) == 0 ? new int?(111) : new int?();
        int? second = (v & 3) == 1 ? new int?(222) : new int?();
        int chained = first ?? second ?? (333 + (w & 0xFF));
        dst[col + 1] = chained;

        // Col 2: ??= on local int? (both null and non-null initial states, RHS side-effect count)
        int? target = (v & 1) == 0 ? new int?() : new int?(77);
        int assignTouches = 0;
        target ??= (++assignTouches) * 10 + (w & 0xF);
        target ??= (++assignTouches) * 100;
        dst[col + 2] = (target ?? -1) + assignTouches * 1000;

        // Col 3: array and non-empty stackalloc span null checks (is null, is not null, == null, != null)
        Span<int> nonEmptySpan = stackalloc int[2];
        nonEmptySpan[0] = v;
        nonEmptySpan[1] = w;
        Span<int> defaultSpan = default;
        int nullFlags = 0;
        nullFlags |= (a is null ? 1 : 0);
        nullFlags |= (a is not null ? 2 : 0);
        nullFlags |= (a == null ? 4 : 0);
        nullFlags |= (a != null ? 8 : 0);
        nullFlags |= (nonEmptySpan == null ? 16 : 0);
        nullFlags |= (nonEmptySpan != null ? 32 : 0);
        nullFlags |= (defaultSpan == null ? 64 : 0);
        nullFlags |= (defaultSpan != null ? 128 : 0);
        dst[col + 3] = nullFlags;
    }

    // -------------------------------------------------------------------------
    // 10. Flattened switch statements (SwitchStatementFlattener / SwitchJumpRewriter):
    //     float classification + 64-bit long switch (goto case, goto default) and shared section consts/locals across Gpu.SyncThreads()
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchFlattenedFloatAndGotoKernel(int[] a, float[] fa, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        float fv = (v & 7) switch
        {
            0 => float.NaN,
            1 => 0.0f,
            2 => -0.0f,
            3 => float.PositiveInfinity,
            4 => float.NegativeInfinity,
            5 => 1.5f,
            6 => -2.5f,
            _ => fa[t]
        };

        // Note: fv != fv tests IEEE-754 NaN without calling unmapped float.IsNaN on C++/CUDA (#948).
        long fTag = (fv != fv) ? long.MinValue
            : (fv == 0.0f ? 0L
            : (fv == float.PositiveInfinity ? long.MaxValue
            : (fv == float.NegativeInfinity ? -5_000_000_000L
            : (fv == 1.5f ? 15L
            : (fv == -2.5f ? -25L : 7L)))));

        int uniformZero = a[0] & 0;
        int fRes = 0;
        switch ((long)uniformZero)
        {
            case 0L:
                Gpu.SyncThreads();
                if (fTag == long.MinValue)
                {
                    goto case long.MinValue;
                }
                if (fTag == 0L)
                {
                    fRes = 100;
                    break;
                }
                if (fTag == long.MaxValue)
                {
                    goto case long.MaxValue;
                }
                if (fTag == -5_000_000_000L)
                {
                    goto case -5_000_000_000L;
                }
                if (fTag == 15L)
                {
                    goto case 15L;
                }
                if (fTag == -25L)
                {
                    goto case -25L;
                }
                goto default;
            case long.MinValue:
                fRes = 900;
                break;
            case long.MaxValue:
                fRes = 200;
                goto case 15L;
            case -5_000_000_000L:
                fRes = 300;
                goto default;
            case 15L:
                fRes += 15;
                break;
            case -25L:
                fRes = -25;
                break;
            default:
                fRes += 7;
                break;
        }

        int iRes = 0;
        switch (uniformZero)
        {
            case 0:
                const int FlatBase = 40;
                int sharedAcrossFlat = FlatBase + (v & 0xF);
                Gpu.SyncThreads();
                if ((v & 3) == 1)
                {
                    goto case 1;
                }
                if ((v & 3) == 2)
                {
                    goto case 2;
                }
                if ((v & 3) == 3)
                {
                    goto default;
                }
                iRes = sharedAcrossFlat;
                break;
            case 1:
                sharedAcrossFlat = FlatBase * 2 + (v & 0xF);
                iRes = sharedAcrossFlat;
                goto case 2;
            case 2:
                iRes += FlatBase;
                break;
            default:
                iRes = -FlatBase;
                break;
        }

        int col = t * 2;
        dst[col + 0] = fRes;
        dst[col + 1] = iRes;
    }

    // =========================================================================
    // REPRODUCTION KERNELS FOR NEWLY DISCOVERED M1 DIVERGENCES (U4-F1 .. U4-F4)
    // =========================================================================

    // -------------------------------------------------------------------------
    // U4-F1 Repro: Empty stack-allocated span local (stackalloc int[0] and []) compared to null
    // In C#, stackalloc int[0] and [] produce an empty Span<int> where (s == null) is true and (s != null) is false.
    // G#'s NullRepresentation.AppendArrayNullTest checks StackArrayLocals.IsStackArrayReference without checking
    // length == 0, emitting the constant 'false' for (s == null) and 'true' for (s != null).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void EmptyStackSpanNullComparisonReproKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        Span<int> sZero = stackalloc int[0];
        Span<int> sCol = [];
        Span<int> sDef = default;

        int col = t * 4;
        dst[col + 0] = sZero == null ? 1 : 0;
        dst[col + 1] = sZero != null ? 1 : 0;
        dst[col + 2] = sCol == null ? 1 : 0;
        dst[col + 3] = (sCol != null ? 10 : 0) + (sDef == null ? 1 : 0) + (a[t] & 0);
    }

    // -------------------------------------------------------------------------
    // U4-F2 Repro: Nullable array-like struct (Memory<int>?, ArraySegment<int>?) wrapping default(T)
    // In C#, converting default(Memory<int>) or default(ArraySegment<int>) to Memory<int>? / ArraySegment<int>?
    // constructs a Nullable<T> with HasValue == true, so `is null` and `== null` are false, while `is not null`,
    // `!= null`, and `.HasValue` are true.
    // G# erases Nullable<T> for array-like value types to GSharpArray<T> (UnwrapNullableArrayLike) and tests
    // `.data == nullptr`, which evaluates to true for default(Memory<int>) / default(ArraySegment<int>).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NullableArrayLikeDefaultValueNullCheckReproKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        Memory<int> emptyMem = default;
        Memory<int>? nonNullEmptyMem = emptyMem;

        ArraySegment<int> emptySeg = default;
        ArraySegment<int>? nonNullEmptySeg = emptySeg;

        int col = t * 4;
        dst[col + 0] = nonNullEmptyMem is null ? 1 : 0;
        dst[col + 1] = nonNullEmptyMem is not null ? 1 : 0;
        dst[col + 2] = (nonNullEmptyMem == null ? 1 : 0) + (nonNullEmptyMem != null ? 10 : 0) + (nonNullEmptyMem.HasValue ? 100 : 0);
        dst[col + 3] = (nonNullEmptySeg is null ? 1 : 0) + (nonNullEmptySeg is not null ? 10 : 0) + (nonNullEmptySeg.HasValue ? 100 : 0) + (a[t] & 0);
    }

    // -------------------------------------------------------------------------
    // U4-F3 Repro: Ref ternary writing a value parameter across a synchronization barrier
    // AstLoweringPipeline.FindWrittenStorageRoot only unwraps IdentifierNameSyntax, ParenthesizedExpressionSyntax,
    // MemberAccessExpressionSyntax, and ElementAccessExpressionSyntax, returning null for ConditionalExpressionSyntax
    // (and RefExpressionSyntax). Consequently, a kernel or device-function value parameter mutated via a ref ternary
    // `((cond ? ref param : ref fallback) = ... / += ... / ++...)` is omitted from LoweredStateMachine.CopiedParameters,
    // so resuming after Gpu.SyncThreads() overwrites the mutated parameter with its original host argument on both
    // the managed and native CPU state-machine paths.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void RefTernaryParameterWriteAcrossBarrierReproKernel(
        int[] conds,
        int[] dst,
        int paramAssign,
        int paramCompound,
        int paramInc,
        int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int c = conds[t];
        int localFallback = 10;

        ((c & 1) == 0 ? ref paramAssign : ref localFallback) = 777;
        ((c & 2) == 0 ? ref paramCompound : ref localFallback) += 100;
        ++((c & 4) == 0 ? ref paramInc : ref localFallback);

        Gpu.SyncThreads();

        int col = t * 4;
        dst[col + 0] = paramAssign;
        dst[col + 1] = paramCompound;
        dst[col + 2] = paramInc;
        dst[col + 3] = localFallback;
    }

    // -------------------------------------------------------------------------
    // U4-F4 Repro: Null-coalescing (??) on narrow Nullable<T> (byte?, ushort?, sbyte?) under var
    // In C#, `mb ?? 0` (where mb is byte? and 0 is a constant in 0..255) has type byte, `mus ?? 0` has type ushort,
    // and `mb ?? usv` (byte? ?? ushort) has type ushort.
    // NullRepresentation.AppendCoalesce emits `return tmp.HasValue ? tmp.Value : right;` in an untyped lambda, where
    // C++ `?:` promotes `unsigned char` / `unsigned short` mixed with `int` or with each other to `int`. Under `var`,
    // incrementing overflows past 255/65535 into int instead of wrapping in byte/ushort.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NarrowNullableCoalesceVarPromotionReproKernel(int[] conds, byte[] ba, ushort[] usa, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int c = conds[t];
        byte? mb = (c & 1) == 0 ? new byte?(255) : new byte?(ba[t]);
        var x = mb ?? 0;
        x++;

        ushort? mus = (c & 2) == 0 ? new ushort?(65535) : new ushort?(usa[t]);
        var y = mus ?? 0;
        y++;

        byte? mbNullForMax = (c & 4) == 0 ? new byte?() : new byte?(255);
        ushort usMax = 65535;
        var z = mbNullForMax ?? usMax;
        z++;

        int col = t * 3;
        dst[col + 0] = x;
        dst[col + 1] = y;
        dst[col + 2] = z;
    }
}
