namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class InlineOutVarTests
:  TranspilerTestBase{
    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [InlineData(5, 25, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 1, 0)]
    [InlineData(-3, 9, 0)]
    public void Test_HappyPath_InlineOutVarInArguments(int val, int expectedOutput, int expectedSuccess)
    {
        int[] input = [val];
        int[] output = [0];
        int[] success = [0];

        InlineOutVarKernels.LaunchHappyPathKernel(input, output, success);

        Assert.Equal(expectedOutput, output[0]);
        Assert.Equal(expectedSuccess, success[0]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [InlineData(4, 26)]  // 4 > 0 -> 16 + 10 = 26
    [InlineData(1, 11)]  // 1 > 0 -> 1 + 10 = 11
    [InlineData(0, -10)] // 0 <= 0 -> 0 - 10 = -10
    [InlineData(-1, -9)] // -1 <= 0 -> 1 - 10 = -9
    [InlineData(-5, 15)] // -5 <= 0 -> 25 - 10 = 15
    public void Test_EdgeCase_IfConditionInlineOutVar(int val, int expectedOutput)
    {
        int[] input = [val];
        int[] output = [0];

        InlineOutVarKernels.LaunchIfConditionKernel(input, output);

        Assert.Equal(expectedOutput, output[0]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [InlineData(4, 6)] // 4 -> steps: 3, 2, 1 -> sum = 6
    [InlineData(1, 0)] // 1 -> step: 0 -> while condition false -> sum = 0
    [InlineData(0, 0)] // 0 -> step: -1 -> while condition false -> sum = 0
    public void Test_EdgeCase_WhileConditionInlineOutVar(int val, int expectedOutput)
    {
        int[] input = [val];
        int[] output = [0];

        InlineOutVarKernels.LaunchWhileConditionKernel(input, output);

        Assert.Equal(expectedOutput, output[0]);
    }
}
