using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A conditional of a stack array local and a kernel array, passed to an array parameter, builds on every path and
/// picks the branch as C# does.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class ConditionalStackArrayTests
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(2, 5)]
    [InlineData(1, 6)]
    public void Conditional_StackArrayAndKernelArray_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchConditionalKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(2, 5)]
    [InlineData(1, 6)]
    public void Conditional_NullableDisabled_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayNullableDisabledKernels.LaunchConditionalKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 6, 5)]
    [InlineData(1, 5, 15)]
    public void Conditional_ReversedAndBothStackArrays_PicksTheBranch(int flag, int first, int second)
    {
        int[] output = [-1, -1];

        ConditionalStackArrayKernels.LaunchReversedAndBothStackKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([first, second], output);
    }
}
