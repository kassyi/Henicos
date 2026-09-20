namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_01_SwitchExpressionTests : TranspilerTestBase
{
    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-01")]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 300)]
    [InlineData(0, -1)]
    [InlineData(-5, -1)]
    [InlineData(100, -1)]
    public void Test_SYN01_SimpleConstantSwitch_Execution(int inputVal, int expectedVal)
    {
        int[] input = [inputVal];
        int[] output = [0];

        SYN_01_SwitchExpressionKernelsWrapper.LaunchSimpleConstantSwitchKernel(input, output, blockDimX: 1);

        Assert.Equal(expectedVal, output[0]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-01")]
    [InlineData(0, 0.0f)]
    [InlineData(1, 3.14159f)]
    [InlineData(2, 2.71828f)]
    [InlineData(99, -1.0f)]
    public void Test_SYN01_FloatingPointReturnSwitch_Execution(int mode, float expectedVal)
    {
        int[] input = [mode];
        float[] output = [0.0f];

        SYN_01_SwitchExpressionKernelsWrapper.LaunchFloatingPointReturnSwitchKernel(input, output, blockDimX: 1);

        Assert.Equal(expectedVal, output[0], precision: 4);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-01")]
    [InlineData(int.MinValue, 1)]
    [InlineData(-1, 2)]
    [InlineData(0, 3)]
    [InlineData(1, 4)]
    [InlineData(int.MaxValue, 5)]
    [InlineData(10, 0)]
    [InlineData(-100, 0)]
    public void Test_SYN01_MultipleArmsBoundarySwitch_Execution(int inputVal, int expectedVal)
    {
        int[] input = [inputVal];
        int[] output = [0];

        SYN_01_SwitchExpressionKernelsWrapper.LaunchMultipleArmsBoundarySwitchKernel(input, output, blockDimX: 1);

        Assert.Equal(expectedVal, output[0]);
    }
}
