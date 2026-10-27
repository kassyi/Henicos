using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="UserLabelKernels"/>. The native and managed test classes share these
/// scenarios and differ only in how a launch runs. Each expected value comes from the same loop in plain C#.
/// </summary>
internal static class UserLabelScenarios
{
    public const int WarpSize = 32;

    public static void LabeledShuffleLoop(Action<Action> run)
    {
        var data = new int[WarpSize];
        for (int i = 0; i < WarpSize; i++)
        {
            data[i] = i;
        }

        run(() => UserLabelKernels.LaunchLabeledShuffleLoopKernel(data, blockDimX: WarpSize));

        for (int lane = 0; lane < WarpSize; lane++)
        {
            int expected = 0;
            for (int round = 0; round < 3; round++)
            {
                // ShuffleDownSync(.., 1) reads the next lane; the last lane reads its own value
                int source = lane + 1 < WarpSize ? lane + 1 : lane;
                expected += source + round;
            }

            Assert.Equal(expected, data[lane]);
        }
    }

    public static void LabeledBarrier(Action<Action> run)
    {
        var output = new int[4];

        run(() => UserLabelKernels.LaunchLabeledBarrierKernel(output, blockDimX: 4));

        Assert.Equal([2, 3, 4, 1], output);
    }
}
