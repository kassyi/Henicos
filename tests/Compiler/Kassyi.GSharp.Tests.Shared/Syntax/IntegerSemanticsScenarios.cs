using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="IntegerSemanticsKernels"/>. The expected values come from the same
/// device functions run as C# on the host, so the property is "the kernel computes what C# computes"; fixed witnesses
/// show that each input reaches the case it is there for (a wrap, a count of 32 or more, a saturated conversion).
/// </summary>
internal static class IntegerSemanticsScenarios
{
    private static readonly int[] s_ints =
        [int.MinValue, int.MinValue + 1, -8, -1, 0, 1, 3, 0x40000000, 65535, int.MaxValue - 1, int.MaxValue];

    // Counts of 32 and more, and negative ones, use only their low bits in C#
    private static readonly int[] s_shifts = [0, 1, 5, 31, 32, 33, 63, 64, 65, -1, -31];

    private static readonly long[] s_longs =
    [
        long.MinValue, long.MinValue + 1, -8L, -1L, 0L, 1L, 3L, int.MaxValue + 1L, 0x4000000000000000L,
        long.MaxValue
    ];

    private static readonly double[] s_doubles =
    [
        3e10, -3e10, double.NaN, -1.0, 300.0, -129.0, 200.0, 40000.0, -40000.0, 70000.0, 3e9, -0.5, 65535.9, 2.5,
        double.PositiveInfinity, double.NegativeInfinity, 1e20, -1e20, 9.3e18, 2e19, -2147483648.0, 2147483647.0,
        -2147483649.0, 4294967295.5
    ];

    public static void IntTriples(Action<Action> run)
    {
        var triples = s_ints.Length * s_ints.Length;
        var o = new int[triples * IntegerSemanticsKernels.IntValuesPerTriple];

        run(() => IntegerSemanticsKernels.LaunchIntTriplesKernel(s_ints, s_shifts, o, blockDimX: (uint)triples));

        var expected = new int[o.Length];
        for (var t = 0; t < triples; t++)
        {
            IntegerSemanticsKernels.IntOperators(
                s_ints[t / s_ints.Length], s_ints[t % s_ints.Length], s_shifts[t % s_shifts.Length], expected, t);
        }

        // a = int.MaxValue (t = 10 * 11 + b): a + 1 < a holds, a << 33 = a << 1
        var maxRow = (s_ints.Length - 1) * s_ints.Length;
        Assert.Equal(1, expected[maxRow * IntegerSemanticsKernels.IntValuesPerTriple + 10]);
        Assert.Equal(-2, expected[maxRow * IntegerSemanticsKernels.IntValuesPerTriple + 7]);

        // a = 3, s = 33 (t = 6 * 11 + 5): 3 << 33 = 6
        var shift33 = 6 * s_ints.Length + 5;
        Assert.Equal(6, expected[shift33 * IntegerSemanticsKernels.IntValuesPerTriple + 4]);

        // a = b = 65535 (t = 8 * 11 + 8): 65535 * 65535 wraps to -131071
        var square = 8 * s_ints.Length + 8;
        Assert.Equal(-131071, expected[square * IntegerSemanticsKernels.IntValuesPerTriple + 2]);

        // a = -8, s = 31 (t = 2 * 11 + 3): -8 >>> 31 = 1
        Assert.Equal(1, expected[(2 * s_ints.Length + 3) * IntegerSemanticsKernels.IntValuesPerTriple + 6]);

        Assert.Equal(expected, o);
    }

    public static void LongTriples(Action<Action> run)
    {
        var triples = s_longs.Length * s_longs.Length;
        var o = new long[triples * IntegerSemanticsKernels.LongValuesPerTriple];

        run(() => IntegerSemanticsKernels.LaunchLongTriplesKernel(s_longs, s_shifts, o, blockDimX: (uint)triples));

        var expected = new long[o.Length];
        for (var t = 0; t < triples; t++)
        {
            IntegerSemanticsKernels.LongOperators(
                s_longs[t / s_longs.Length], s_longs[t % s_longs.Length], s_shifts[t % s_shifts.Length], expected, t);
        }

        // a = long.MaxValue: a + 1 < a holds, a << 65 = a << 1
        var maxRow = (s_longs.Length - 1) * s_longs.Length;
        Assert.Equal(1L, expected[maxRow * IntegerSemanticsKernels.LongValuesPerTriple + 9]);
        Assert.Equal(-2L, expected[maxRow * IntegerSemanticsKernels.LongValuesPerTriple + 7]);

        // a = 3, s = 64 (t = 6 * 10 + 2): 3L << 64 = 3
        Assert.Equal(3L, expected[(6 * s_longs.Length + 2) * IntegerSemanticsKernels.LongValuesPerTriple + 4]);

        Assert.Equal(expected, o);
    }

    public static void Conversions(Action<Action> run)
    {
        var floats = new float[s_doubles.Length];
        for (var i = 0; i < floats.Length; i++)
        {
            floats[i] = (float)s_doubles[i];
        }

        var o = new long[s_doubles.Length * IntegerSemanticsKernels.ConversionsPerInput];

        run(() => IntegerSemanticsKernels.LaunchConversionsKernel(floats, s_doubles, o,
            blockDimX: (uint)s_doubles.Length));

        var expected = new long[o.Length];
        for (var i = 0; i < s_doubles.Length; i++)
        {
            IntegerSemanticsKernels.ConvertOperators(floats[i], s_doubles[i], expected, i);
        }

        // 3e10, -3e10 and NaN (inputs 0 to 2): (int) saturates, NaN is 0
        const int per = IntegerSemanticsKernels.ConversionsPerInput;
        Assert.Equal([int.MaxValue, int.MinValue, 0L], [expected[0], expected[per], expected[2 * per]]);

        // -1 (input 3): (uint) saturates to 0, (byte) is the low byte of the int32 -1
        Assert.Equal(0L, expected[3 * per + 1]);
        Assert.Equal(255L, expected[3 * per + 5]);

        Assert.Equal(expected, o);
    }

    public static void SignedBuiltIns(Action<Action> run)
    {
        var negative = new int[10];
        var wide = new long[10];

        run(() => IntegerSemanticsKernels.LaunchSignedBuiltInsKernel(negative, wide, blockDimX: 2));

        Assert.Equal([1, 0, 1, 1, 1, 1, 1, 1, 1, 1], negative);
        Assert.Equal([-1L, 0L, -1L, -1L, -1L, -1L, -1L, -1L, -2L, -1L], wide);
    }

    public static void LoopCounters(Action<Action> run)
    {
        int[] starts = [int.MaxValue - 3, int.MinValue, 0, int.MaxValue - 4];
        int[] ends = [int.MaxValue, int.MinValue + 2, 0, int.MaxValue];
        var counts = new int[starts.Length * 4];

        run(() => IntegerSemanticsKernels.LaunchLoopCountersKernel(starts, ends, counts, blockDimX: (uint)starts.Length));

        Assert.Equal(
            [
                3, 3, 2, int.MaxValue - 1,
                2, 2, 1, int.MinValue,
                0, 0, 0, 0,
                4, 4, 2, int.MaxValue - 2
            ],
            counts);
    }

    public static void AcrossBarrier(Action<Action> run)
    {
        int[] a = [int.MaxValue, 3, -1];
        int[] s = [33, 32, 31];
        var o = new int[a.Length * 4];

        run(() => IntegerSemanticsKernels.LaunchAcrossBarrierKernel(a, s, o, blockDimX: (uint)a.Length));

        Assert.Equal(
            [
                1, 0, int.MinValue, int.MinValue,
                0, 4, 4, int.MaxValue,
                0, 0, 0, int.MaxValue
            ],
            o);
    }
}
