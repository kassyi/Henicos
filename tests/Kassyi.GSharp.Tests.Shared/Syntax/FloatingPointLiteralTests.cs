namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
public class FloatingPointLiteralTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_FloatLiteralWithoutDecimalPoint_Execution()
    {
        float[] values = [1f, 2f];

        FloatingPointLiteralKernels.LaunchFloatLiteralKernel(values, blockDimX: 2);

        Assert.Equal([13f, 15f], values);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DoubleLiteralWithSuffix_Execution()
    {
        double[] values = [1d, 2d];

        FloatingPointLiteralKernels.LaunchDoubleLiteralKernel(values, blockDimX: 2);

        Assert.Equal([4.5d, 6.5d], values);
    }
}
