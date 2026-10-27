using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="UIntNegationAndConditionalKernels"/>. The expected values come from the
/// same device functions run as C# on the host; a few fixed values show that the reference computes in
/// <see langword="long"/>.
/// </summary>
internal static class UIntNegationAndConditionalScenarios
{
    public static void Negation(Action<Action> run)
    {
        uint[] us = [0u, 1u, 7u, 0x80000000u, 0xFFFFFFFFu];
        var values = new long[us.Length * UIntNegationAndConditionalKernels.NegationValues];

        run(() => UIntNegationAndConditionalKernels.LaunchNegationKernel(us, values, blockDimX: (uint)us.Length));

        var expected = new long[values.Length];
        for (var i = 0; i < us.Length; i++)
        {
            UIntNegationAndConditionalKernels.Negate(us[i], expected, i);
        }

        // -7u = -7, -0xFFFFFFFFu * 2 = -8589934590
        Assert.Equal(-7L, expected[2 * UIntNegationAndConditionalKernels.NegationValues]);
        Assert.Equal(-8589934590L, expected[4 * UIntNegationAndConditionalKernels.NegationValues + 1]);
        Assert.Equal(-7L, expected[2 * UIntNegationAndConditionalKernels.NegationValues + 2]);

        Assert.Equal(expected, values);
    }

    public static void Conditional(Action<Action> run)
    {
        uint[] us = [7u, 0u, 0xFFFFFFFFu, 0x80000000u];
        int[] ns = [-8, int.MinValue, -1, int.MaxValue];
        var threads = us.Length * 2;
        var longs = new long[threads * UIntNegationAndConditionalKernels.ConditionalValues];
        var doubles = new double[threads];

        run(() => UIntNegationAndConditionalKernels.LaunchConditionalKernel(us, ns, longs, doubles, blockDimX: (uint)threads));

        var expectedLongs = new long[longs.Length];
        var expectedDoubles = new double[doubles.Length];
        for (var tid = 0; tid < threads; tid++)
        {
            UIntNegationAndConditionalKernels.Choose(
                tid % 2 == 0, us[tid / 2], ns[tid / 2], expectedLongs, expectedDoubles, tid);
        }

        // Thread 1: c = false, n = -8; c ? u : -1 is -1
        Assert.Equal(-8L, expectedLongs[UIntNegationAndConditionalKernels.ConditionalValues]);
        Assert.Equal(-1L, expectedLongs[UIntNegationAndConditionalKernels.ConditionalValues + 1]);
        Assert.Equal(-8.0, expectedDoubles[1]);

        Assert.Equal(expectedLongs, longs);
        Assert.Equal(expectedDoubles, doubles);
    }
}
