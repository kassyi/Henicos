namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A file-local type in a device function whose body the managed path does not copy runs on every dialect.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class FileLocalTypeTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchApplyKernel_FileLocalStructInDeviceFunction_ScalesEveryElement()
    {
        int[] values = [0, 1, 2, 3, 4];

        FileLocalTypeKernels.LaunchApplyKernel(values, 3, blockDimX: 5);

        Assert.Equal([1, 4, 7, 10, 13], values);
    }
}
