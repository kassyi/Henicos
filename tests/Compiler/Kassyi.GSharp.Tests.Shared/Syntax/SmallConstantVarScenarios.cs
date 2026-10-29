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

    public static void LocalConst(Action<Action> run)
    {
        var output = new int[1];

        run(() => SmallConstantVarKernels.LaunchLocalConstKernel(output, blockDimX: 1));

        Assert.Equal([0], output);
    }
}
