namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
public class LocalFunctionParameterTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_ReadOnlyLocalFunctionParameter_Execution()
    {
        int[] table = [1, 2, 3, 4];
        int scale = 10;
        int[] output = new int[4];

        LocalFunctionParameterKernels.LaunchReadOnlyLocalFunctionParameterKernel(output, table, scale, blockDimX: 4);

        Assert.Equal(10, output[0]);
        Assert.Equal(20, output[1]);
        Assert.Equal(30, output[2]);
        Assert.Equal(40, output[3]);
    }
}
