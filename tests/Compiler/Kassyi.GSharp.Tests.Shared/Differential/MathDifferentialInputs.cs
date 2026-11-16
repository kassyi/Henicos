#nullable enable
using System;
using System.Numerics;

namespace Kassyi.GSharp.Tests.Shared.Differential;

internal static class MathDifferentialInputs
{
    public static readonly float[] SingleRoundBoundaries =
    [
        .. DiffValues.SingleBoundaries,
        -1.5f, 3.5f, -3.5f, 2f, 3f, 8388607.5f, -8388607.5f, 16777216f,
        MathF.BitDecrement(0.5f), MathF.BitIncrement(-0.5f), MathF.BitDecrement(-0.5f),
    ];

    public static readonly double[] DoubleBoundaries =
    [
        0.0, -0.0, 1.0, -1.0, 2.0, -2.0, 3.0, -3.0, 0.25, -0.25,
        0.5, -0.5, 1.5, -1.5, 2.5, -2.5, 3.5, -3.5,
        Math.BitDecrement(0.5), Math.BitIncrement(-0.5), Math.BitDecrement(-0.5),
        double.Epsilon, -double.Epsilon,
        BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        -BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        2.2250738585072014e-308, -2.2250738585072014e-308,
        double.MaxValue, double.MinValue, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
        4503599627370495.5, -4503599627370495.5, 4503599627370496.0, 9007199254740992.0, 1e15 + 0.5,
    ];

    // Excludes int.MinValue so Math.Abs(int) does not throw OverflowException in the C# oracle (#947)
    public static readonly int[] IntSafeAbsBoundaries =
    [
        0, 1, -1, 2, -2, 3, -3, int.MaxValue, int.MaxValue - 1, int.MinValue + 1,
        16777216, 16777217, -16777217, 16777219, -16777219,
        0x7FFF, -0x7FFF, 0x8000, -0x8000, 0xFFFF, 0x10000, 0x40000000, -0x40000000,
    ];

    public static readonly short[] ShortSafeAbsBoundaries =
    [
        0, 1, -1, 2, -2, short.MaxValue, short.MaxValue - 1, short.MinValue + 1, 127, -127, 128, -128, 255, -255,
    ];

    public static readonly sbyte[] SbyteSafeAbsBoundaries =
    [
        0, 1, -1, 2, -2, sbyte.MaxValue, sbyte.MaxValue - 1, sbyte.MinValue + 1, 64, -64,
    ];

    public static readonly float[] TrigExpSpecialSingles =
    [
        0.0f, -0.0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
    ];

    public static readonly float[] LogSpecialSingles =
    [
        0.0f, -0.0f, 1.0f, -1.0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
    ];

    public static readonly (float Base, float Exp)[] PowSpecialSinglePairs =
    [
        (0.0f, 3.0f),
        (-0.0f, 3.0f),
        (0.0f, -3.0f),
        (-0.0f, -3.0f),
        (0.0f, 2.0f),
        (-0.0f, 2.0f),
        (0.0f, -2.0f),
        (-0.0f, -2.0f),
        (-1.0f, float.PositiveInfinity),
        (-1.0f, float.NegativeInfinity),
        (0.5f, float.PositiveInfinity),
        (0.5f, float.NegativeInfinity),
        (2.0f, float.PositiveInfinity),
        (2.0f, float.NegativeInfinity),
        (-2.0f, 3.0f),
        (-2.0f, 0.5f),
        (1.0f, float.NaN),
        (float.NaN, 0.0f),
        (float.NaN, -0.0f),
    ];

    public static readonly double[] TrigExpSpecialDoubles =
    [
        0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
    ];

    public static readonly double[] LogSpecialDoubles =
    [
        0.0, -0.0, 1.0, -1.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
    ];

    public static readonly (double Base, double Exp)[] PowSpecialDoublePairs =
    [
        (0.0, 3.0),
        (-0.0, 3.0),
        (0.0, -3.0),
        (-0.0, -3.0),
        (0.0, 2.0),
        (-0.0, 2.0),
        (0.0, -2.0),
        (-0.0, -2.0),
        (-1.0, double.PositiveInfinity),
        (-1.0, double.NegativeInfinity),
        (0.5, double.PositiveInfinity),
        (0.5, double.NegativeInfinity),
        (2.0, double.PositiveInfinity),
        (2.0, double.NegativeInfinity),
        (-2.0, 3.0),
        (-2.0, 0.5),
        (1.0, double.NaN),
        (double.NaN, 0.0),
        (double.NaN, -0.0),
    ];

    public static readonly float[] TrigDomainSingleBoundaries =
    [
        0.0f, -0.0f, float.Epsilon, -float.Epsilon, 1.17549435e-38f, -1.17549435e-38f,
        1.0e-20f, -1.0e-20f, 1.0e-5f, -1.0e-5f, 0.1f, -0.1f, 0.25f, -0.25f,
        0.5f, -0.5f, 0.7853982f, -0.7853982f, 1.0f, -1.0f, 1.2f, -1.2f,
        2.0f, -2.0f, 2.5f, -2.5f, 3.0f, -3.0f, 3.14f, -3.14f,
    ];

    public static readonly float[] ExpDomainSingleBoundaries =
    [
        0.0f, -0.0f, float.Epsilon, -float.Epsilon, 0.5f, -0.5f, 1.0f, -1.0f,
        2.0f, -2.0f, 10.0f, -10.0f, 50.0f, -50.0f, 88.0f, 88.722839f,
        -87.0f, -87.3365f, -95.0f /* subnormal output */, -102.0f, -103.2789f, -104.0f,
    ];

    public static readonly float[] LogDomainSingleBoundaries =
    [
        float.Epsilon, 2.0f * float.Epsilon, BitConverter.Int32BitsToSingle(0x007F_FFFF),
        1.17549435e-38f, 1.0e-20f, 1.0e-5f, 0.1f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f,
        MathF.E, 10.0f, 100.0f, 1.0e20f, float.MaxValue,
    ];

    public static readonly double[] TrigDomainDoubleBoundaries =
    [
        0.0, -0.0, double.Epsilon, -double.Epsilon, 2.2250738585072014e-308, -2.2250738585072014e-308,
        1.0e-100, -1.0e-100, 1.0e-10, -1.0e-10, 0.1, -0.1, 0.25, -0.25,
        0.5, -0.5, 0.7853981633974483, -0.7853981633974483, 1.0, -1.0,
        2.0, -2.0, 2.5, -2.5, 3.0, -3.0, 3.14, -3.14,
    ];

    public static readonly double[] ExpDomainDoubleBoundaries =
    [
        0.0, -0.0, double.Epsilon, -double.Epsilon, 0.5, -0.5, 1.0, -1.0,
        2.0, -2.0, 10.0, -10.0, 100.0, -100.0, 709.0, 709.782712893384,
        -708.0, -708.3964185322641, -720.0 /* double subnormal output */, -744.0, -745.0,
    ];

    public static readonly double[] LogDomainDoubleBoundaries =
    [
        double.Epsilon, 2.0 * double.Epsilon, BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        2.2250738585072014e-308, 1.0e-200, 1.0e-20, 0.1, 0.5, 0.75, 1.0, 1.5, 2.0,
        Math.E, 10.0, 100.0, 1.0e200, double.MaxValue,
    ];

    public static float[] SingleRoundValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, SingleRoundBoundaries, DiffValues.NextSingle);

    public static double[] DoubleValues(DiffRandom rng, int count) =>
        DiffValues.Values(rng, count, DoubleBoundaries, NextDouble);

    public static float NextSubnormalSingle(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return DiffValues.SubnormalSingleBoundaries[rng.NextBelow(DiffValues.SubnormalSingleBoundaries.Length)];
        }

        uint sign = rng.NextBool() ? 0x8000_0000u : 0u;
        uint mantissa = rng.NextUInt32() & 0x007F_FFFFu;
        return BitConverter.UInt32BitsToSingle(sign | mantissa);
    }

    public static double NextSubnormalDouble(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return DiffValues.SubnormalDoubleBoundaries[rng.NextBelow(DiffValues.SubnormalDoubleBoundaries.Length)];
        }

        ulong sign = rng.NextBool() ? 0x8000_0000_0000_0000UL : 0UL;
        ulong mantissa = rng.NextUInt64() & 0x000F_FFFF_FFFF_FFFFUL;
        return BitConverter.UInt64BitsToDouble(sign | mantissa);
    }

    public static float NextSafeTrigSingle(DiffRandom rng)
    {
        double u = rng.NextUInt32() / (double)uint.MaxValue;
        float x = (float)((u * 2.0 - 1.0) * 3.14);
        if (MathF.Abs(MathF.Abs(x) - MathF.PI * 0.5f) < 0.02f)
        {
            x += 0.05f;
        }

        return x;
    }

    public static double NextSafeTrigDouble(DiffRandom rng)
    {
        double u = rng.NextUInt64() / (double)ulong.MaxValue;
        double x = (u * 2.0 - 1.0) * 3.14;
        if (Math.Abs(Math.Abs(x) - Math.PI * 0.5) < 0.02)
        {
            x += 0.05;
        }

        return x;
    }

    public static float NextPositiveSingle(DiffRandom rng)
    {
        uint exp = (uint)rng.NextBelow(254); // 0 (subnormal) .. 253 (normal)
        uint mant = rng.NextUInt32() & 0x007F_FFFFu;
        if (exp == 0 && mant == 0)
        {
            mant = 1;
        }

        return BitConverter.UInt32BitsToSingle((exp << 23) | mant);
    }

    public static double NextPositiveDouble(DiffRandom rng)
    {
        ulong exp = (ulong)rng.NextBelow(2046); // 0 (subnormal) .. 2045 (normal)
        ulong mant = rng.NextUInt64() & 0x000F_FFFF_FFFF_FFFFUL;
        if (exp == 0 && mant == 0)
        {
            mant = 1;
        }

        return BitConverter.UInt64BitsToDouble((exp << 52) | mant);
    }

    public static double NextDouble(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return DoubleBoundaries[rng.NextBelow(DoubleBoundaries.Length)];
        }

        if (rng.NextBool())
        {
            return BitConverter.UInt64BitsToDouble(rng.NextUInt64());
        }

        ulong sign = rng.NextBool() ? 0x8000000000000000ul : 0ul;
        ulong exponent = (ulong)(1023 - 40 + rng.NextBelow(101));
        ulong mantissa = rng.NextUInt64() >> 12;
        return BitConverter.UInt64BitsToDouble(sign | (exponent << 52) | mantissa);
    }

    public static (float[] Left, float[] Right) SinglePairs(DiffRandom rng, int rows) =>
        DiffValues.Pairs(rng, rows, DiffValues.SingleBoundaries, DiffValues.NextSingle);

    public static (double[] Left, double[] Right) DoublePairs(DiffRandom rng, int rows) =>
        DiffValues.Pairs(rng, rows, DoubleBoundaries, NextDouble);

    public static (float[] Single, double[] Double) SingleDoublePairs(DiffRandom rng, int rows)
    {
        var single = new float[rows];
        var wide = new double[rows];
        int nf = DiffValues.SingleBoundaries.Length;
        int nd = DoubleBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                single[r] = DiffValues.SingleBoundaries[r / nd % nf];
                wide[r] = DoubleBoundaries[r % nd];
            }
            else
            {
                single[r] = DiffValues.NextSingle(rng);
                wide[r] = NextDouble(rng);
            }
        }

        return (single, wide);
    }

    // The second bound of each row (a boundary on seed 0, otherwise next), and the two sorted into (min, max)
    public static (T[] Min, T[] Max) OrderedBounds<T>(
        DiffRandom rng,
        int rows,
        T[] firstBound,
        T[] boundaries,
        Func<DiffRandom, T> next
    )
        where T : IComparisonOperators<T, T, bool>
    {
        var min = new T[rows];
        var max = new T[rows];
        int n = boundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            T lo = firstBound[r];
            T hi = rng.IsBoundarySeed ? boundaries[(r * 7 + 3) % n] : next(rng);
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            min[r] = lo;
            max[r] = hi;
        }

        return (min, max);
    }

    public static int NextSafeAbsInt32(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return IntSafeAbsBoundaries[rng.NextBelow(IntSafeAbsBoundaries.Length)];
        }

        int v = rng.NextInt32();
        return v == int.MinValue ? int.MinValue + 1 : v;
    }

    public static int[] SafeAbsInt32Values(DiffRandom rng, int rows) =>
        DiffValues.Values(rng, rows, IntSafeAbsBoundaries, NextSafeAbsInt32);

    public static (int[] Left, int[] Right) SafeAbsInt32Pairs(DiffRandom rng, int rows) =>
        DiffValues.Pairs(rng, rows, IntSafeAbsBoundaries, NextSafeAbsInt32);

    // short.MinValue is left out: Math.Abs of it throws in C#
    public static short[] SafeAbsInt16Values(DiffRandom rng, int rows) =>
        DiffValues.Mixed(rng, rows, ShortSafeAbsBoundaries, static r =>
        {
            short v = unchecked((short)r.NextInt32());
            return v == short.MinValue ? (short)(short.MinValue + 1) : v;
        });

    // sbyte.MinValue is left out: Math.Abs of it throws in C#
    public static sbyte[] SafeAbsSByteValues(DiffRandom rng, int rows) =>
        DiffValues.Mixed(rng, rows, SbyteSafeAbsBoundaries, static r =>
        {
            sbyte v = unchecked((sbyte)r.NextInt32());
            return v == sbyte.MinValue ? (sbyte)(sbyte.MinValue + 1) : v;
        });
}
