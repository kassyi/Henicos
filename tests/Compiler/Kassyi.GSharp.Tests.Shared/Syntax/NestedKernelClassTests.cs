namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A kernel of a private nested class builds and runs like any other.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class NestedKernelClassTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchNestedFill_PrivateClassInPrivateClass_WritesEveryElement()
    {
        int[] output = new int[8];

        NestedKernelClassKernels.LaunchNestedFill(output);

        Assert.Equal([1, 4, 7, 10, 13, 16, 19, 22], output);
    }
}
