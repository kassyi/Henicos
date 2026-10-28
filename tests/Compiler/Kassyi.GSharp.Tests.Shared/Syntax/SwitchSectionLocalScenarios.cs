using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="SwitchSectionLocalKernels"/>. The native and managed test classes share
/// these scenarios and differ only in how a launch runs.
/// </summary>
internal static class SwitchSectionLocalScenarios
{
    public const int WarpSize = 32;

    public static void InitializedInFirstSection(Action<Action> run, int flag)
    {
        var output = new int[1];

        run(() => SwitchSectionLocalKernels.LaunchInitializedInFirstSectionKernel(output, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? 1 : 6, output[0]);
    }

    public static void SharedStructAndVar(Action<Action> run)
    {
        var data = CreateSequence();

        run(() => SwitchSectionLocalKernels.LaunchSharedStructAndVarKernel(data, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = (i % 3) switch
            {
                // pair = (i, 2), scaled = 10i, shared = 10i + 7, later = shared + 2
                0 => 10 * i + 9,
                // pair = (1, i), scaled = 100i, shared = scaled + 3
                1 => 100 * i + 3,
                _ => i + 1000
            };
            Assert.Equal(expected, data[i]);
        }
    }

    public static void GotoCaseAndNestedSwitch(Action<Action> run)
    {
        var data = CreateSequence();

        run(() => SwitchSectionLocalKernels.LaunchGotoCaseAndNestedSwitchKernel(data, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            // Even: case 0 writes 2i, case 1 reads it back and adds 3. Odd: i plus 4 * 10
            Assert.Equal(i % 2 == 0 ? 2 * i + 3 : i + 40, data[i]);
        }
    }

    public static void SectionConstant(Action<Action> run, int flag)
    {
        var output = new int[1];

        run(() => SwitchSectionLocalKernels.LaunchSectionConstantKernel(output, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? 41 : 42, output[0]);
    }

    private static int[] CreateSequence()
    {
        var data = new int[WarpSize];
        for (int i = 0; i < WarpSize; i++)
        {
            data[i] = i;
        }

        return data;
    }
}
