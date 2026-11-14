using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A conditional that has a stack array local on a branch builds on every path, picks the branch as C# does, and keeps
/// the writes through it (<see cref="ConditionalStackArrayKernels"/>).
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
    [Scenario(TestScenario.HappyPath)]
    [InlineData(2, 6)]
    [InlineData(1, 5)]
    public void Reversed_KernelArrayAndStackArray_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchReversedKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 5)]
    [InlineData(1, 15)]
    public void DifferentSizes_TwoStackArrays_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchDifferentSizesKernel([flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 9, 0)]
    [InlineData(1, 0, 9)]
    public void EqualSizes_CalleeWritesThePickedArray_SeesTheWriteInThatArrayOnly(int flag, int first, int second)
    {
        int[] output = [-1, -1];

        ConditionalStackArrayKernels.LaunchEqualSizesWriterKernel([flag], output, blockDimX: 1);

        Assert.Equal([first, second], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 5)]
    [InlineData(1, 6)]
    public void NonConstantSize_StackArrayAndKernelArray_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchNonConstantSizeKernel([1, 2, 3], [flag], 4, output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 0)]
    [InlineData(1, 6)]
    public void EmptySpan_ZeroLengthStackSpanAndKernelArray_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchEmptySpanKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 2, 5)]
    [InlineData(2, 1, 34)]
    [InlineData(1, 2, 6)]
    public void NestedTrueSide_ConditionalOnTheTrueArm_PicksTheBranch(int outer, int inner, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchNestedTrueSideKernel([1, 2, 3], [outer, inner], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, 5)]
    [InlineData(3, 6)]
    public void SwitchArm_StackArrayArm_PicksTheArm(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchSwitchArmKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 5)]
    [InlineData(1, 6)]
    public void NotNullArm_GpuNotNullOfStackArray_PicksTheBranch(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchNotNullArmKernel([1, 2, 3], [flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 32)]
    [InlineData(1, 35)]
    public void VarLength_ConditionalOfEqualSizedStackArrays_KeepsLengthAndElements(int flag, int expected)
    {
        int[] output = [-1];

        ConditionalStackArrayKernels.LaunchVarLengthKernel([flag], output, blockDimX: 1);

        Assert.Equal([expected], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 42, 5)]
    [InlineData(1, 2, 42)]
    public void MemberAccess_ParenthesizedConditional_WritesThePickedArray(int flag, int first, int second)
    {
        int[] output = [-1, -1, -1];

        ConditionalStackArrayKernels.LaunchMemberAccessKernel([flag], output, blockDimX: 1);

        Assert.Equal([3, first, second], output);
    }
}
