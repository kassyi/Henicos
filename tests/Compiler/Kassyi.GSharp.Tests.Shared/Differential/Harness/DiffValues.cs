#nullable enable
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
    public static readonly int[] ShiftCounts = [0, 1, 7, 31, 32, 33, 63, 64, 65, -1, -31, -32, int.MinValue, int.MaxValue];

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

    private static T[] Values<T>(DiffRandom rng, int count, T[] boundaries, Func<DiffRandom, T> next)
    {
        var values = new T[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = rng.IsBoundarySeed ? boundaries[i % boundaries.Length] : next(rng);
        }

        return values;
    }

    private static (T[] Left, T[] Right) Pairs<T>(DiffRandom rng, int rows, T[] boundaries, Func<DiffRandom, T> next)
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
