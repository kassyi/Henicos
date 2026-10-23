namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class InternalAccessibilityTests
{
    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void InternalKernelClass_GeneratedLauncherCompilesAndRuns()
    {
        int[] input = [1, 2, 3];
        int[] output = [0, 0, 0];

        InternalAccessibilityKernels.LaunchDoubleKernel(input, output, blockDimX: 3);

        Assert.Equal([2, 4, 6], output);
    }
}
