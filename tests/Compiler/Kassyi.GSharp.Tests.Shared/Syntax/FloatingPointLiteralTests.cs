namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class FloatingPointLiteralTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void FloatLiteralWithoutDecimalPoint_DeviceExecution_MatchesExpectedValues()
    {
        float[] values = [1f, 2f];

        FloatingPointLiteralKernels.LaunchFloatLiteralKernel(values, blockDimX: 2);

        Assert.Equal([13f, 15f], values);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DoubleLiteralWithSuffix_DeviceExecution_MatchesExpectedValues()
    {
        double[] values = [1d, 2d];

        FloatingPointLiteralKernels.LaunchDoubleLiteralKernel(values, blockDimX: 2);

        Assert.Equal([4.5d, 6.5d], values);
    }
}
