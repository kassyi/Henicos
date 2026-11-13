namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A kernel of a private struct parameter builds, and its private launcher marshals the struct.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class PrivateStructParameterTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchScale_PrivateStructWithArray_WritesThroughTheArray()
    {
        int[] values = [1, 2, 3, 4, 5, 6];

        PrivateStructParameterKernels.LaunchScale(values, 3);

        Assert.Equal([3, 7, 11, 15, 19, 23], values);
    }
}
