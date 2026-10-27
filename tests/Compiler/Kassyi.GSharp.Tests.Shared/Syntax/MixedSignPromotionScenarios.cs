using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="MixedSignPromotionKernels"/>. The expected values come from the same
/// device functions run as C# on the host, so the property is "the kernel computes what C# computes" (C# spec §12.4.7.3);
/// a few fixed values show that the reference itself computes in <see langword="long"/>.
/// </summary>
internal static class MixedSignPromotionScenarios
{
    private static readonly uint[] s_unsigned = [0u, 1u, 7u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu];

    // No 0: dividing by zero is undefined on the GPU and outside this test
    private static readonly int[] s_ints = [int.MinValue, -8, -1, 1, int.MaxValue];
    private static readonly short[] s_shorts = [short.MinValue, -1, 1];
    private static readonly sbyte[] s_sbytes = [sbyte.MinValue, -1, 1];

    public static void IntPairs(Action<Action> run)
    {
        var pairs = s_unsigned.Length * s_ints.Length;
        var (values, flags) = Allocate(pairs);

        run(() => MixedSignPromotionKernels.LaunchIntPairsKernel(s_unsigned, s_ints, values, flags, blockDimX: (uint)pairs));

        var (expectedValues, expectedFlags) = Allocate(pairs);
        for (var pair = 0; pair < pairs; pair++)
        {
            MixedSignPromotionKernels.IntOperators(
                s_unsigned[pair / s_ints.Length], s_ints[pair % s_ints.Length], expectedValues, expectedFlags, pair);
        }

        // u = 7, n = -8 (pair 2 * 5 + 1): u + n = -1, n / u = -1, u > n
        var witness = 2 * s_ints.Length + 1;
        Assert.Equal(-1L, expectedValues[witness * MixedSignPromotionKernels.ValuesPerPair]);
        Assert.Equal(-1L, expectedValues[witness * MixedSignPromotionKernels.ValuesPerPair + 11]);
        Assert.Equal(-1L, expectedValues[witness * MixedSignPromotionKernels.ValuesPerPair + 16]);
        Assert.Equal(1, expectedFlags[witness * MixedSignPromotionKernels.FlagsPerPair + 3]);

        // u = 0 (pair 0): (u & n) - 1 = -1
        Assert.Equal(-1L, expectedValues[17]);

        Assert.Equal(expectedValues, values);
        Assert.Equal(expectedFlags, flags);
    }

    public static void ShortPairs(Action<Action> run)
    {
        var pairs = s_unsigned.Length * s_shorts.Length;
        var (values, flags) = Allocate(pairs);

        run(() => MixedSignPromotionKernels.LaunchShortPairsKernel(s_unsigned, s_shorts, values, flags, blockDimX: (uint)pairs));

        var (expectedValues, expectedFlags) = Allocate(pairs);
        for (var pair = 0; pair < pairs; pair++)
        {
            MixedSignPromotionKernels.ShortOperators(
                s_unsigned[pair / s_shorts.Length], s_shorts[pair % s_shorts.Length], expectedValues, expectedFlags, pair);
        }

        // u = 7, s = -1 (pair 2 * 3 + 1): u + s = 6, u * s = -7, u > s
        var witness = 2 * s_shorts.Length + 1;
        Assert.Equal(-7L, expectedValues[witness * MixedSignPromotionKernels.ValuesPerPair + 2]);
        Assert.Equal(1, expectedFlags[witness * MixedSignPromotionKernels.FlagsPerPair + 3]);

        Assert.Equal(expectedValues, values);
        Assert.Equal(expectedFlags, flags);
    }

    public static void SBytePairs(Action<Action> run)
    {
        var pairs = s_unsigned.Length * s_sbytes.Length;
        var (values, flags) = Allocate(pairs);

        run(() => MixedSignPromotionKernels.LaunchSBytePairsKernel(s_unsigned, s_sbytes, values, flags, blockDimX: (uint)pairs));

        var (expectedValues, expectedFlags) = Allocate(pairs);
        for (var pair = 0; pair < pairs; pair++)
        {
            MixedSignPromotionKernels.SByteOperators(
                s_unsigned[pair / s_sbytes.Length], s_sbytes[pair % s_sbytes.Length], expectedValues, expectedFlags, pair);
        }

        // u = 0, b = -128 (pair 0): u + b = -128, u >= b
        Assert.Equal(-128L, expectedValues[0]);
        Assert.Equal(1, expectedFlags[5]);

        Assert.Equal(expectedValues, values);
        Assert.Equal(expectedFlags, flags);
    }

    public static void ConstantOperands(Action<Action> run)
    {
        uint[] us = [0u, 7u, 0xFFFFFFFFu];
        var values = new long[us.Length * 3];
        var flags = new int[us.Length];

        run(() => MixedSignPromotionKernels.LaunchConstantOperandsKernel(us, values, flags, blockDimX: (uint)us.Length));

        // u - 8 and u + 8u stay uint and wrap; u + -8 is long; u == -1 is always false
        Assert.Equal(
            [
                4294967288L, -8L, 8L,
                4294967295L, -1L, 15L,
                4294967287L, 4294967287L, 7L
            ],
            values);
        Assert.Equal([0, 0, 0], flags);
    }

    public static void AcrossBarrier(Action<Action> run)
    {
        uint[] us = [7u, 0u, 0xFFFFFFFFu, 0x80000000u];
        int[] ns = [-8, int.MinValue, -1, 1];
        var values = new long[us.Length * 2];
        var flags = new int[us.Length];

        run(() => MixedSignPromotionKernels.LaunchAcrossBarrierKernel(us, ns, values, flags, blockDimX: (uint)us.Length));

        // u + n, then TakeFive(ref k) + k = 3 + (tid - 5)
        Assert.Equal(
            [
                -1L, -2L,
                -2147483648L, -1L,
                4294967294L, 0L,
                0x80000001L, 1L
            ],
            values);
        Assert.Equal([0, 0, 0, 0], flags);
    }

    private static (long[] Values, int[] Flags) Allocate(int pairs) =>
        (new long[pairs * MixedSignPromotionKernels.ValuesPerPair], new int[pairs * MixedSignPromotionKernels.FlagsPerPair]);
}
