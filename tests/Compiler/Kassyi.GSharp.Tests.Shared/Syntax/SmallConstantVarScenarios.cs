using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="SmallConstantVarKernels"/>. The expected values are the same
/// statements evaluated by C#; the native and managed test classes share these scenarios and differ only in how a
/// launch runs.
/// </summary>
internal static class SmallConstantVarScenarios
{
    public static void Increment(Action<Action> run)
    {
        var output = new int[8];

        run(() => SmallConstantVarKernels.LaunchIncrementKernel(output, blockDimX: 1));

        var b = SmallConstantVarKernels.ByteMax;
        b++;
        var sbMax = SmallConstantVarKernels.SByteMax;
        sbMax++;
        var sbMin = SmallConstantVarKernels.SByteMin;
        sbMin--;
        var sMax = SmallConstantVarKernels.ShortMax;
        sMax++;
        var sMin = SmallConstantVarKernels.ShortMin;
        --sMin;
        var usMax = SmallConstantVarKernels.UShortMax;
        ++usMax;
        var usZero = SmallConstantVarKernels.UShortZero;
        usZero--;
        var compound = SmallConstantVarKernels.ByteMax;
        compound += 2;

        // 0, -128, 127, -32768, 32767, 0, 65535, 1
        Assert.Equal([b, sbMax, sbMin, sMax, sMin, usMax, usZero, compound], output);
    }

    public static void EnumIncrement(Action<Action> run)
    {
        var output = new int[3];

        run(() => SmallConstantVarKernels.LaunchEnumIncrementKernel(output, blockDimX: 1));

        var last = SmallConstantByteEnum.Last;
        last++;
        var highest = SmallConstantShortEnum.Highest;
        highest++;
        var lowest = SmallConstantShortEnum.Lowest;
        lowest--;

        // 0, -32768, 32767
        Assert.Equal([(int)last, (int)highest, (int)lowest], output);
    }

    // The inputs of NarrowConstantUses: four switch values, two Math values, a short, the selector of the conditionals
    // and two more switch values. A short of 32766 steps to 32767, which the conditional that picks it steps past
    // short.MaxValue: with the selector 1 (mixedFalse) and -1 (mixedTrue)
    private static readonly int[][] s_narrowInputs =
    [
        [0, 255, -1, 'a', -5, 1000, 300, 1, 1, -1],
        [255, 3, 1, 'b', 1000, -5, 32767, 1, 0, 1],
        [7, 0, 5, 'c', 0, 100, -300, 1, 1, 0],
        [0, 255, -1, 'a', -5, 1000, 32766, 1, 0, -1],
        [0, 255, -1, 'a', -5, 1000, 32766, -1, 1, 1]
    ];

    public static void NarrowUses(Action<Action> run)
    {
        foreach (var input in s_narrowInputs)
        {
            var output = new int[SmallConstantVarKernels.NarrowUseCount];

            run(() => SmallConstantVarKernels.LaunchNarrowConstantUsesKernel(input, output, blockDimX: 1));

            var expected = new int[output.Length];
            SmallConstantVarKernels.NarrowConstantUses(input, expected);
            Assert.Equal(expected, output);
        }

        // Witnesses of the first input: goto case reaches the next label, Math.Max(255, -5) is 255, the true branch of
        // the conditional is the short 255 and steps to 256, the false one 301, the byte and short parameters wrap, the
        // bool and the negative enum switches jump to the next case
        var first = new int[SmallConstantVarKernels.NarrowUseCount];
        SmallConstantVarKernels.NarrowConstantUses(s_narrowInputs[0], first);
        Assert.Equal(
            [10, 11, 20, 30, 31, 40, 41, 255, -32768, 255, 256, 302, 0, 256, -32768, 50, 51, 60, 61],
            first);

        // The conditional that picks the short 32767 wraps as a short: the false branch (selector 1) and the true one
        // (selector -1)
        var falseBranch = new int[SmallConstantVarKernels.NarrowUseCount];
        SmallConstantVarKernels.NarrowConstantUses(s_narrowInputs[3], falseBranch);
        Assert.Equal([256, -32768], falseBranch.AsSpan(10, 2).ToArray());
        var trueBranch = new int[SmallConstantVarKernels.NarrowUseCount];
        SmallConstantVarKernels.NarrowConstantUses(s_narrowInputs[4], trueBranch);
        Assert.Equal([-32768, 256], trueBranch.AsSpan(10, 2).ToArray());
    }

    public static void LocalConst(Action<Action> run)
    {
        var output = new int[1];

        run(() => SmallConstantVarKernels.LaunchLocalConstKernel(output, blockDimX: 1));

        Assert.Equal([0], output);
    }
}
