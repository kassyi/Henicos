using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

internal static partial class SwitchSuspensionScenarios
{
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
}
