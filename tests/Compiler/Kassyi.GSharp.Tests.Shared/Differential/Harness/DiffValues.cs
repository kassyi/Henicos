#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Input values of the differential tests. Seed 0 (<see cref="DiffRandom.IsBoundarySeed"/>) returns the boundary tables
/// in a fixed order, so every boundary reaches the kernel in every run (the coverage witnesses of property-test-design
/// §4); any other seed draws a boundary with probability 1/2 and random bits otherwise.
/// </summary>
public static class DiffValues
{
    /// <summary>The <see cref="int"/> boundaries: signs, the ends of the range, 16-bit and 31-bit edges, shift widths.</summary>
    public static readonly int[] Int32Boundaries =
    [
        0, 1, -1, 2, -2, int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1,
        0x7FFF, 0x8000, 0xFFFF, 0x10000, 0x40000000, -0x40000000, 31, 32, 33, 63, 64, 65,
    ];

    /// <summary>Shift counts around the 32-bit and 64-bit masks, including negative counts.</summary>
    public static readonly int[] ShiftCounts = [0, 1, 7, 31, 32, 33, 63, 64, 65, -1, -31, -32, -63, -64, int.MinValue, int.MaxValue];

    /// <summary>
    /// <see cref="float"/> subnormal and subnormal-to-normal boundary values (including signed zeros, minimum/maximum
    /// subnormals, and minimum normal values) for verifying FTZ-free IEEE 754 arithmetic.
    /// </summary>
    public static readonly float[] SubnormalSingleBoundaries =
    [
        0.0f,
        -0.0f,
        float.Epsilon,
        -float.Epsilon,
        2.0f * float.Epsilon,
        -2.0f * float.Epsilon,
        BitConverter.Int32BitsToSingle(0x0040_0000),
        -BitConverter.Int32BitsToSingle(0x0040_0000),
        BitConverter.Int32BitsToSingle(0x007F_FFFE),
        -BitConverter.Int32BitsToSingle(0x007F_FFFE),
        BitConverter.Int32BitsToSingle(0x007F_FFFF) /* MaxSubnormal */,
        -BitConverter.Int32BitsToSingle(0x007F_FFFF),
        BitConverter.Int32BitsToSingle(0x0080_0000) /* MinNormal = 1.17549435e-38f */,
        -BitConverter.Int32BitsToSingle(0x0080_0000),
        BitConverter.Int32BitsToSingle(0x0080_0001),
        -BitConverter.Int32BitsToSingle(0x0080_0001),
        0.5f,
        -0.5f,
        1.0f,
        -1.0f,
        2.0f,
        -2.0f,
    ];

    /// <summary>
    /// <see cref="double"/> subnormal and subnormal-to-normal boundary values (including signed zeros, minimum/maximum
    /// subnormals, and minimum normal values) for verifying FTZ-free IEEE 754 arithmetic.
    /// </summary>
    public static readonly double[] SubnormalDoubleBoundaries =
    [
        0.0,
        -0.0,
        double.Epsilon,
        -double.Epsilon,
        2.0 * double.Epsilon,
        -2.0 * double.Epsilon,
        BitConverter.Int64BitsToDouble(0x0008_0000_0000_0000L),
        -BitConverter.Int64BitsToDouble(0x0008_0000_0000_0000L),
        BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFEL),
        -BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFEL),
        BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL) /* MaxSubnormal */,
        -BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        BitConverter.Int64BitsToDouble(0x0010_0000_0000_0000L) /* MinNormal = 2.2250738585072014e-308 */,
        -BitConverter.Int64BitsToDouble(0x0010_0000_0000_0000L),
        BitConverter.Int64BitsToDouble(0x0010_0000_0000_0001L),
        -BitConverter.Int64BitsToDouble(0x0010_0000_0000_0001L),
        0.5,
        -0.5,
        1.0,
        -1.0,
        2.0,
        -2.0,
    ];

    /// <summary>
    /// <see cref="float"/> conversion edge boundaries covering all 9 integer target types (<c>sbyte</c>, <c>byte</c>,
    /// <c>short</c>, <c>ushort</c>, <c>char</c>, <c>int</c>, <c>uint</c>, <c>long</c>, <c>ulong</c>), <c>[int.MaxValue+1, uint.MaxValue]</c>
    /// narrow saturation edges (<c>3e9f</c>), and special values.
    /// </summary>
    public static readonly float[] ConversionEdgeSingles =
    [
        0f,
        -0f,
        1f,
        -1f,
        0.5f,
        -0.5f,
        1.5f,
        -1.5f,
        2.5f,
        -2.5f,
        float.Epsilon,
        -float.Epsilon,
        1.17549435e-38f,
        -1.17549435e-38f,
        127f,
        128f,
        -128f,
        -129f,
        255f,
        255.5f,
        256f,
        32767f,
        32768f,
        -32768f,
        -32769f,
        65535f,
        65535.5f,
        65536f,
        2147483520f /* largest float < 2^31 */,
        2147483648f,
        -2147483648f,
        -2147483904f,
        3_000_000_000f /* in (int.MaxValue, uint.MaxValue) */,
        -3_000_000_000f,
        4294967040f,
        4294967296f,
        9.22337149e18f,
        9.22337204e18f,
        -9.22337204e18f,
        1.8446743e19f,
        1.8446744e19f,
        3e10f,
        -3e10f,
        float.MaxValue,
        float.MinValue,
        float.PositiveInfinity,
        float.NegativeInfinity,
        float.NaN,
        BitConverter.UInt32BitsToSingle(0xFFC0_0001u),
    ];

    /// <summary>
    /// <see cref="double"/> conversion edge boundaries covering all 9 integer target types, <c>±1 ULP</c> around saturation
    /// thresholds (<c>-2147483649.0</c>, <c>2147483648.0</c>, <c>-1.0</c>, <c>4294967296.0</c>, <c>±2^63</c>, <c>2^64</c>),
    /// and <c>double -&gt; float</c> overflow/subnormal rounding boundaries.
    /// </summary>
    public static readonly double[] ConversionEdgeDoubles =
    [
        0.0,
        -0.0,
        1.0,
        -1.0,
        0.5,
        -0.5,
        1.5,
        -1.5,
        2.5,
        -2.5,
        double.Epsilon,
        -double.Epsilon,
        1.401298464324817e-45 * 0.5 /* float subnormal tie */,
        -1.401298464324817e-45 * 0.5,
        1.401298464324817e-45 /* float.Epsilon in double */,
        -1.401298464324817e-45,
        1.1754943508222875e-38 /* float MinNormal in double */,
        -1.1754943508222875e-38,
        2.2250738585072014e-308,
        -2.2250738585072014e-308,
        Math.BitIncrement(-1.0),
        -128.0,
        -128.9,
        -129.0,
        127.0,
        128.0,
        255.0,
        255.9,
        256.0,
        -32768.0,
        -32769.0,
        32767.0,
        32768.0,
        65535.0,
        65535.9,
        65536.0,
        -2147483649.0,
        Math.BitIncrement(-2147483649.0),
        -2147483648.0,
        Math.BitDecrement(2147483648.0),
        2147483647.0,
        2147483648.0,
        3_000_000_000.0,
        -3_000_000_000.0,
        4294967295.0,
        Math.BitDecrement(4294967296.0),
        4294967296.0,
        Math.BitDecrement(9223372036854775808.0),
        9223372036854775808.0,
        -9223372036854775808.0,
        Math.BitDecrement(-9223372036854775808.0),
        Math.BitDecrement(18446744073709551616.0),
        18446744073709551616.0,
        3.4028234663852886e38 /* float.MaxValue in double */,
        -3.4028234663852886e38,
        Math.BitDecrement(3.4028235677973366e38) /* largest double rounding to float.MaxValue */,
        3.4028235677973366e38 /* smallest double rounding to +Infinity in float: 2^128 * (1 - 2^-25) */,
        -3.4028235677973366e38,
        double.MaxValue,
        double.MinValue,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NaN,
        BitConverter.UInt64BitsToDouble(0xFFF8_0000_0000_0001UL),
    ];

    /// <summary>
    /// 64-bit signed integer rounding tie and double-rounding witness values for <c>long -&gt; float</c> and <c>long -&gt; double</c>.
    /// </summary>
    public static readonly long[] Int64RoundingTieBoundaries =
    [
        0L,
        1L,
        -1L,
        2L,
        -2L,
        (1L << 24) - 1,
        1L << 24,
        (1L << 24) + 1,
        -((1L << 24) + 1),
        (1L << 25) + 1,
        -((1L << 25) + 1),
        int.MaxValue,
        int.MinValue,
        (1L << 53) - 1,
        1L << 53,
        (1L << 53) + 1,
        -((1L << 53) + 1),
        (1L << 53) + 2,
        -((1L << 53) + 2),
        (1L << 53) + 3,
        -((1L << 53) + 3),
        (1L << 54) + 2,
        -((1L << 54) + 2),
        (1L << 54) + 4,
        -((1L << 54) + 4),
        (1L << 53) + (1L << 28) + 1 /* double-rounding witness */,
        -((1L << 53) + (1L << 28) + 1),
        0x4000_003F_FFFF_FE01L,
        -0x4000_003F_FFFF_FE01L,
        0x4000_0040_0000_0001L,
        -0x4000_0040_0000_0001L,
        0x4000_00BF_FFFF_FE01L,
        -0x4000_00BF_FFFF_FE01L,
        0x7FFF_FFBF_FFFF_FFFFL,
        0x7FFF_FFC0_0000_0000L,
        0x7FFF_FFC0_0000_0001L,
        -0x7FFF_FFC0_0000_0001L,
        long.MaxValue - 1L,
        long.MaxValue,
        long.MinValue + 1L,
        long.MinValue,
    ];

    /// <summary>
    /// 64-bit unsigned integer rounding tie and double-rounding witness values for <c>ulong -&gt; float</c> and <c>ulong -&gt; double</c>.
    /// </summary>
    public static readonly ulong[] UInt64RoundingTieBoundaries =
    [
        0UL,
        1UL,
        2UL,
        255UL,
        65535UL,
        (1UL << 24) - 1,
        1UL << 24,
        (1UL << 24) + 1,
        (1UL << 24) + 2,
        (1UL << 24) + 3,
        (1UL << 25) + 1,
        (1UL << 25) + 3,
        uint.MaxValue,
        (1UL << 32) + 1,
        (1UL << 53) - 1,
        1UL << 53,
        (1UL << 53) + 1,
        (1UL << 53) + 2,
        (1UL << 53) + 3,
        (1UL << 54) + 1,
        (1UL << 54) + 2,
        (1UL << 54) + 3,
        (1UL << 54) + 4,
        (1UL << 53) + (1UL << 28) + 1 /* ulong -> double -> float double-rounding witness */,
        0x8000_007F_FFFF_FC00UL,
        0x8000_007F_FFFF_FC01UL,
        0x8000_0080_0000_0000UL,
        0x8000_0080_0000_0001UL,
        0x8000_017F_FFFF_FC00UL,
        0x8000_017F_FFFF_FC01UL,
        0x8000_0180_0000_0000UL,
        0x8000_0180_0000_0001UL,
        0xFFFF_FF00_0000_0000UL,
        0xFFFF_FF00_0000_0001UL,
        0xFFFF_FF80_0000_0000UL /* float tie-to-even */,
        0xFFFF_FF80_0000_0001UL /* float sticky-bit round-up */,
        (ulong)long.MaxValue,
        (ulong)long.MaxValue + 1UL,
        ulong.MaxValue - 1UL,
        ulong.MaxValue,
    ];

    /// <summary>32-bit unsigned corner cases for <see cref="System.Numerics.BitOperations"/>.</summary>
    public static readonly uint[] BitOperationUInt32Boundaries =
    [
        0u,
        1u,
        2u,
        3u,
        0x0000_8000u,
        0x0001_0000u,
        0x5555_5555u,
        0xAAAA_AAAAu,
        0x7FFF_FFFFu,
        0x8000_0000u,
        0x8000_0001u,
        0x1234_5678u,
        0xF000_0000u,
        uint.MaxValue - 1u,
        uint.MaxValue,
    ];

    /// <summary>64-bit unsigned corner cases for <see cref="System.Numerics.BitOperations"/>.</summary>
    public static readonly ulong[] BitOperationUInt64Boundaries =
    [
        0UL,
        1UL,
        2UL,
        3UL,
        0x0000_0000_8000_0000UL,
        0x0000_0001_0000_0000UL,
        0x5555_5555_5555_5555UL,
        0xAAAA_AAAA_AAAA_AAAAUL,
        0x7FFF_FFFF_FFFF_FFFFUL,
        0x8000_0000_0000_0000UL,
        0x8000_0000_0000_0001UL,
        0x1234_5678_9ABC_DEF0UL,
        0xF000_0000_0000_0000UL,
        ulong.MaxValue - 1UL,
        ulong.MaxValue,
    ];

    /// <summary>
    /// The <see cref="float"/> boundaries: signed zeros, subnormals, the normal range, infinities, NaN, rounding ties and
    /// the edges of the integer conversions (2^31, 2^32, 2^63, 2^64 and the floats next to them).
    /// </summary>
    public static readonly float[] SingleBoundaries =
    [
        0f, -0f, 1f, -1f, 0.5f, -0.5f, 1.5f, 2.5f, -2.5f, float.Epsilon, -float.Epsilon, 1.17549435e-38f /* min normal */,
        float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
        2147483520f /* largest float < 2^31 */, 2147483648f, -2147483648f, -2147483904f, 4294967040f, 4294967296f,
        9.22337204e18f, -9.22337204e18f, 1.8446743e19f, 255.5f, 256f, -129f, 65535.5f, 3e10f, -3e10f,
    ];

    /// <summary>Returns <paramref name="count"/> <see cref="int"/> inputs: seed 0 repeats the boundary table in order.</summary>
    /// <param name="rng">The generator; its seed selects the boundary table or random values.</param>
    /// <param name="count">The number of values.</param>
    public static int[] Int32(DiffRandom rng, int count) => Values(rng, count, Int32Boundaries, NextInt32);

    /// <summary>
    /// Returns <paramref name="rows"/> pairs of <see cref="int"/> inputs. Seed 0 gives the cross product of the boundary
    /// table, row by row (row <c>r</c> is <c>(B[r / n % n], B[r % n])</c>; 441 rows cover it exactly once); any other
    /// seed draws both sides independently.
    /// </summary>
    /// <param name="rng">The generator.</param>
    /// <param name="rows">The number of pairs.</param>
    public static (int[] Left, int[] Right) Int32Pairs(DiffRandom rng, int rows) => Pairs(rng, rows, Int32Boundaries, NextInt32);

    /// <summary>Returns <paramref name="count"/> <see cref="float"/> inputs: seed 0 repeats the boundary table in order.</summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    public static float[] Single(DiffRandom rng, int count) => Values(rng, count, SingleBoundaries, NextSingle);

    /// <summary>A boundary with probability 1/2, otherwise 32 random bits.</summary>
    /// <param name="rng">The generator.</param>
    public static int NextInt32(DiffRandom rng) =>
        rng.NextBool() ? Int32Boundaries[rng.NextBelow(Int32Boundaries.Length)] : rng.NextInt32();

    /// <summary>
    /// A boundary with probability 1/2; otherwise a random bit pattern (any class: NaN payloads, subnormals, infinities)
    /// or a random normal value of magnitude 2^-30 to 2^40 with a random sign, with probability 1/4 each.
    /// </summary>
    /// <param name="rng">The generator.</param>
    public static float NextSingle(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return SingleBoundaries[rng.NextBelow(SingleBoundaries.Length)];
        }

        if (rng.NextBool())
        {
            return BitConverter.UInt32BitsToSingle(rng.NextUInt32());
        }

        uint sign = rng.NextBool() ? 0x80000000u : 0u;
        uint exponent = (uint)(127 - 30 + rng.NextBelow(71));
        uint mantissa = rng.NextUInt32() >> 9;
        return BitConverter.UInt32BitsToSingle(sign | (exponent << 23) | mantissa);
    }

    /// <summary>The <see cref="short"/> boundaries of <see cref="Int16"/>.</summary>
    public static readonly short[] Int16Boundaries = [0, 1, -1, short.MinValue, short.MaxValue, -128, 127, 255, 256];

    /// <summary>The <see cref="ushort"/> boundaries of <see cref="UInt16"/>.</summary>
    public static readonly ushort[] UInt16Boundaries = [0, 1, 127, 128, 255, 256, 32767, 32768, ushort.MaxValue];

    /// <summary>The <see cref="byte"/> boundaries of <see cref="Byte"/>.</summary>
    public static readonly byte[] ByteBoundaries = [0, 1, 2, 127, 128, 250, 254, byte.MaxValue];

    /// <summary>The <see cref="sbyte"/> boundaries of <see cref="SByte"/>.</summary>
    public static readonly sbyte[] SByteBoundaries = [0, 1, -1, sbyte.MinValue, sbyte.MaxValue, -127, 126];

    /// <summary>
    /// Returns <paramref name="count"/> inputs: seed 0 repeats <paramref name="boundaries"/> in order; any other seed draws
    /// a boundary with probability 1/2 and <paramref name="random"/> otherwise.
    /// </summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    /// <param name="boundaries">The boundary table of the test.</param>
    /// <param name="random">Draws a value that is not taken from the table.</param>
    public static T[] Mixed<T>(DiffRandom rng, int count, T[] boundaries, Func<DiffRandom, T> random) =>
        Values(rng, count, boundaries, r => r.NextBool() ? boundaries[r.NextBelow(boundaries.Length)] : random(r));

    /// <summary>
    /// Returns <paramref name="count"/> inputs: the boundary at the same position (<c>i % n</c>) on seed 0, and with
    /// probability 1/2 on any other seed; <paramref name="random"/> otherwise.
    /// </summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    /// <param name="boundaries">The boundary table.</param>
    /// <param name="random">Draws a value that is not taken from the table.</param>
    public static T[] Cycled<T>(DiffRandom rng, int count, T[] boundaries, Func<DiffRandom, T> random)
    {
        var values = new T[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = rng.IsBoundarySeed || rng.NextBool() ? boundaries[i % boundaries.Length] : random(rng);
        }

        return values;
    }

    /// <summary>Returns <paramref name="count"/> values of <paramref name="random"/> on every seed, without boundaries.</summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    /// <param name="random">Draws one value.</param>
    public static T[] Random<T>(DiffRandom rng, int count, Func<DiffRandom, T> random)
    {
        var values = new T[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = random(rng);
        }

        return values;
    }

    /// <summary><see cref="Cycled{T}"/> over <see cref="Int16Boundaries"/> with 16 random bits.</summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    public static short[] Int16(DiffRandom rng, int count) =>
        Cycled(rng, count, Int16Boundaries, static r => unchecked((short)r.NextInt32()));

    /// <summary><see cref="Cycled{T}"/> over <see cref="UInt16Boundaries"/> with 16 random bits.</summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    public static ushort[] UInt16(DiffRandom rng, int count) =>
        Cycled(rng, count, UInt16Boundaries, static r => unchecked((ushort)r.NextUInt32()));

    /// <summary><see cref="Cycled{T}"/> over <see cref="ByteBoundaries"/> with 8 random bits.</summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    public static byte[] Byte(DiffRandom rng, int count) =>
        Cycled(rng, count, ByteBoundaries, static r => unchecked((byte)r.NextUInt32()));

    /// <summary><see cref="Cycled{T}"/> over <see cref="SByteBoundaries"/> with 8 random bits.</summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    public static sbyte[] SByte(DiffRandom rng, int count) =>
        Cycled(rng, count, SByteBoundaries, static r => unchecked((sbyte)r.NextInt32()));

    /// <summary>
    /// Returns <paramref name="count"/> inputs: seed 0 repeats <paramref name="boundaries"/> in order; any other seed calls
    /// <paramref name="next"/>, which decides itself whether to draw a boundary.
    /// </summary>
    /// <param name="rng">The generator.</param>
    /// <param name="count">The number of values.</param>
    /// <param name="boundaries">The boundary table.</param>
    /// <param name="next">Draws one value on a seed other than 0.</param>
    public static T[] Values<T>(DiffRandom rng, int count, T[] boundaries, Func<DiffRandom, T> next)
    {
        var values = new T[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = rng.IsBoundarySeed ? boundaries[i % boundaries.Length] : next(rng);
        }

        return values;
    }

    /// <summary>
    /// Returns <paramref name="rows"/> pairs: seed 0 gives the cross product of <paramref name="boundaries"/> row by row
    /// (row <c>r</c> is <c>(B[r / n % n], B[r % n])</c>); any other seed calls <paramref name="next"/> for each side.
    /// </summary>
    /// <param name="rng">The generator.</param>
    /// <param name="rows">The number of pairs.</param>
    /// <param name="boundaries">The boundary table.</param>
    /// <param name="next">Draws one value on a seed other than 0.</param>
    public static (T[] Left, T[] Right) Pairs<T>(DiffRandom rng, int rows, T[] boundaries, Func<DiffRandom, T> next)
    {
        var left = new T[rows];
        var right = new T[rows];
        int n = boundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                left[r] = boundaries[r / n % n];
                right[r] = boundaries[r % n];
            }
            else
            {
                left[r] = next(rng);
                right[r] = next(rng);
            }
        }

        return (left, right);
    }
}
