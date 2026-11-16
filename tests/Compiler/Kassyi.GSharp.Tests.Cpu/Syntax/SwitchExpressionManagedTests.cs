#nullable enable
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed copy of a switch expression without a discard arm throws what C# throws when no arm matches: a
/// <see cref="SwitchExpressionException"/> that carries the unmatched value.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SwitchExpressionManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void NoDiscardArmSwitch_UnmatchedInputOnManagedPath_ThrowsWithTheUnmatchedValue()
    {
        int[] input = [5];
        int[] output = [0];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var exception = Assert.Throws<SwitchExpressionException>(() =>
                SwitchExpressionKernels.LaunchNoDiscardArmSwitchKernel(input, output, blockDimX: 1));

            Assert.Equal(5, exception.UnmatchedValue);
        }
    }
}
