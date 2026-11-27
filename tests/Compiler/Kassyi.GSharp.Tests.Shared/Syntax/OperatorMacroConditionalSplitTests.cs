#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential tests for user-defined <c>&amp;&amp;</c> and <c>||</c> over operator macros whose right (or left)
/// operand reaches a barrier or <c>Warp.ActiveMask()</c> split point, across all available paths (managed, native,
/// stack variants, and CUDA).
/// </summary>
[Category(TestCategory.Differential)]
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class OperatorMacroConditionalSplitTests
{
    private static readonly int[] s_input = [1, -1, 0, 2, -3, 0, 5, -2];

    private static void AssertOnEveryPath(
        Action<int[], int[]> launch,
        int[] expectedOutput,
        int[] expectedCount)
    {
        foreach (DiffPathRunner path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            int[] output = new int[expectedOutput.Length];
            int[] count = new int[expectedCount.Length];
            path.Run(() => launch(output, count));
            Assert.True(
                expectedOutput.SequenceEqual(output),
                $"{path.Name} output: expected [{string.Join(", ", expectedOutput)}], got [{string.Join(", ", output)}]");
            Assert.True(
                expectedCount.SequenceEqual(count),
                $"{path.Name} count: expected [{string.Join(", ", expectedCount)}], got [{string.Join(", ", count)}]");
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UserDefinedAnd_RightHasBarrier_MatchesCSharpValues()
    {
        int[] expectedOutput = [12, -1, 2, 22, -3, 2, 52, -2];
        int[] expectedCount = [1, 0, 1, 1, 0, 1, 1, 0];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchAndBarrierKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UserDefinedOr_RightHasActiveMask_MatchesCSharpValues()
    {
        int[] expectedOutput = [1, 82, 182, 2, -118, 182, 5, -18];
        int[] expectedCount = [0, 1, 1, 0, 1, 1, 0, 1];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchOrMaskKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UserDefinedAnd_RightHasActiveMask_MatchesCSharpValues()
    {
        int[] expectedOutput = [119, -1, 109, 129, -3, 109, 159, -2];
        int[] expectedCount = [1, 0, 1, 1, 0, 1, 1, 0];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchAndMaskKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedAnd_AsOperandOfOuterMacro_MatchesCSharpValues()
    {
        int[] expectedOutput = [11902, -98, 10902, 12902, -298, 10902, 15902, -198];
        int[] expectedCount = [1, 0, 1, 1, 0, 1, 1, 0];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchOperandOfMacroKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedAnd_RightOperandIsMacro_MatchesCSharpValues()
    {
        int[] expectedOutput = [1102, -1, 1092, 1112, -3, 1092, 1142, -2];
        int[] expectedCount = [1, 0, 1, 1, 0, 1, 1, 0];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchRightIsMacroKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedOrAndChain_RightHasActiveMask_MatchesCSharpValues()
    {
        int[] expectedOutput = [1, -102, -1, 2, -114, 196, 5, 16];
        int[] expectedCount = [0, 0, 0, 0, 1, 1, 0, 1];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchChainKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedAnd_LeftOperandWithSideEffect_EvaluatesLeftOnce()
    {
        int[] expectedOutput = [119, -1, 109, 129, -3, 109, 159, -2];
        int[] expectedCount = [101, 100, 101, 101, 100, 101, 101, 100];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchLeftOnceKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedAnd_BothOperandsHoldSplit_EvaluatesLeftAndRightWithMask()
    {
        int[] expectedOutput = [275, 275, 275, 275, 275, 275, 275, 275];
        int[] expectedCount = [2, 2, 2, 2, 2, 2, 2, 2];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchLeftAndRightSplitKernel(
                output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedAnd_TargetIndexWrittenByLeft_EvaluatesTargetBeforeLeft()
    {
        int[] input = [-1, 1];
        int[] expectedValues = [-1, 0, 12, 0];
        int[] expectedIndex = [1, 1];
        int[] expectedCount = [0, 1];

        foreach (DiffPathRunner path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            TruthValue[] values = new TruthValue[4];
            int[] index = new int[2];
            int[] count = new int[2];
            path.Run(() => OperatorMacroConditionalSplitKernels.LaunchTargetOrderSplitKernel(
                input, values, index, count, blockDimX: 2));
            int[] actualValues = [values[0].Value, values[1].Value, values[2].Value, values[3].Value];
            Assert.True(
                expectedValues.SequenceEqual(actualValues),
                $"{path.Name} values: expected [{string.Join(", ", expectedValues)}], got [{string.Join(", ", actualValues)}]");
            Assert.True(
                expectedIndex.SequenceEqual(index),
                $"{path.Name} index: expected [{string.Join(", ", expectedIndex)}], got [{string.Join(", ", index)}]");
            Assert.True(
                expectedCount.SequenceEqual(count),
                $"{path.Name} count: expected [{string.Join(", ", expectedCount)}], got [{string.Join(", ", count)}]");
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UserDefinedAnd_TruthOperatorTakesInParameter_MatchesCSharpValues()
    {
        int[] expectedOutput = [119, -1, 109, 129, -3, 109, 159, -2];
        int[] expectedCount = [1, 0, 1, 1, 0, 1, 1, 0];

        AssertOnEveryPath(
            (output, count) => OperatorMacroConditionalSplitKernels.LaunchAndInMaskKernel(
                s_input, output, count, blockDimX: 8),
            expectedOutput,
            expectedCount);
    }
}
