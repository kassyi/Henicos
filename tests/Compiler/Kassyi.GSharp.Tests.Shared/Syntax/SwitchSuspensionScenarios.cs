using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="SwitchSuspensionKernels"/> (#708). The native and managed test classes
/// share these scenarios and differ only in how a launch runs. The expected values are computed per lane on the host.
/// </summary>
internal static class SwitchSuspensionScenarios
{
    public const int WarpSize = 32;

    public static void BarrierInSection(Action<Action> run, int k)
    {
        const int Count = 64;
        var data = new int[Count];
        var output = new int[Count];

        run(() => SwitchSuspensionKernels.LaunchBarrierInSectionKernel(data, output, k, Count, blockDimX: Count));

        for (int i = 0; i < Count; i++)
        {
            int neighbour = (i + 1) % Count;
            Assert.Equal(k == 0 ? neighbour + 1 : 100 + neighbour, output[i]);
        }
    }

    public static void ShuffleInSeveralSections(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchShuffleInSeveralSectionsKernel(output, flag, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = flag switch
            {
                0 => (i ^ 1) * 10,
                // ShuffleDownSync keeps the caller's own value when the source lane is past the warp
                1 or 3 => (i + flag < WarpSize ? (i + flag) * 10 : i * 10) + flag,
                _ => (i ^ 2) * 10 + (i ^ 4) * 10
            };
            Assert.Equal(expected, output[i]);
        }
    }

    public static void NestedSwitch(Action<Action> run, int outer, int inner)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchNestedSwitchKernel(output, outer, inner, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = (outer, inner) switch
            {
                (0, 0) => (i ^ 1) + 1000,
                (0, _) => i + 1100,
                (_, 0) => (i ^ 3) + 5,
                _ => (i ^ 3) + 7
            };
            Assert.Equal(expected, output[i]);
        }
    }

    public static void GotoCase(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchGotoCaseKernel(output, flag, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = flag switch
            {
                // case 0 reads lane i ^ 1, then case 2 reads that value from lane i ^ 2
                0 => (i ^ 3) * 2,
                // case 1 adds 50, then the default section reads lane i ^ 4
                1 => (i ^ 4) + 51,
                2 => (i ^ 2) * 2,
                _ => (i ^ 4) + 1
            };
            Assert.Equal(expected, output[i]);
        }
    }

    public static void SwitchInLoop(Action<Action> run, int rounds)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchSwitchInLoopKernel(output, rounds, blockDimX: WarpSize));

        var expected = new int[WarpSize];
        for (int i = 0; i < WarpSize; i++)
        {
            expected[i] = i;
        }

        var previous = new int[WarpSize];
        for (int round = 0; round < rounds; round++)
        {
            Array.Copy(expected, previous, WarpSize);
            for (int i = 0; i < WarpSize; i++)
            {
                expected[i] = (round % 3) switch
                {
                    0 => previous[i ^ 1] + 10,
                    // continue skips the += 10 after the switch
                    1 => previous[i] + 1,
                    _ => previous[i ^ 2] * 2 + 10
                };
            }
        }

        Assert.Equal(expected, output);
    }

    public static void GoverningEvaluatedOnce(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchGoverningEvaluatedOnceKernel(output, flag, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int value = flag switch
            {
                0 => i ^ 1,
                1 => i ^ 2,
                _ => i
            };
            Assert.Equal(value * 10 + flag + 1, output[i]);
        }
    }

    public static void SectionConstantAcrossBarrier(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchSectionConstantAcrossBarrierKernel(output, flag, blockDimX: WarpSize));

        // case 0: (byte)(255 + 1) == 0; default: (byte)(255 + 1) + 255 == 255
        Assert.All(output, value => Assert.Equal(flag == 0 ? 0 : 255, value));
    }

    public static void SectionConstantChain(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchSectionConstantChainKernel(output, flag, blockDimX: WarpSize));

        Assert.All(output, value => Assert.Equal(flag switch { 0 => 3, 1 => 6, _ => 9 }, value));
    }

#if GSHARP_CPU
    public static void NaNCase(Action<Action> run, float selector)
    {
        var output = new int[WarpSize];

        run(() => SwitchNaNCaseKernels.LaunchNaNCaseKernel(output, selector, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            // C# matches case float.NaN for a NaN value (the switch compares with float.IsNaN)
            int expected = float.IsNaN(selector) ? i ^ 1 : selector == 1f ? i + 100 : i + 1000;
            Assert.Equal(expected, output[i]);
        }
    }
#endif

    public static void SectionConstantCaseLabel(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSuspensionKernels.LaunchSectionConstantCaseLabelKernel(output, flag, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = flag switch
            {
                0 => i ^ 1,
                1 => (i ^ 2) + 100,
                _ => i + 1000
            };
            Assert.Equal(expected, output[i]);
        }
    }
}
