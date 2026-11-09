using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution of <see cref="NullableStackArrayKernels"/>, compiled with the nullable context on (#1543): a stack array
/// passed to a non-nullable array parameter compiles and the callee reads it.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class NullableStackArrayTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackArrayPassedToANonNullableParameter_IsRead()
    {
        int[] input = [1, 2, 3];
        var output = new int[3];

        NullableStackArrayKernels.LaunchStackArrayArgumentKernel(input, output, blockDimX: 1);

        Assert.Equal([4, 9, 6], output);
    }
}
