using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution of <see cref="NullableStackArrayKernels"/>, compiled with the nullable context on (#1543): a stack array
/// passed to a non-nullable array parameter compiles and the callee reads it; a kernel parameter is the control.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class NullableStackArrayTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackArrayPassedToANonNullableParameter_IsRead()
    {
        var output = new int[1];

        NullableStackArrayKernels.LaunchStackArrayArgumentKernel(output, blockDimX: 1);

        Assert.Equal(4, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackArrayBesideAKernelParameter_IsRead()
    {
        int[] input = [1, 2, 3];
        var output = new int[1];

        NullableStackArrayKernels.LaunchStackArrayWithParameterKernel(input, output, blockDimX: 1);

        Assert.Equal(9, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KernelParameterPassedToANonNullableParameter_IsRead()
    {
        int[] input = [1, 2, 3];
        var output = new int[1];

        NullableStackArrayKernels.LaunchParameterArgumentKernel(input, output, blockDimX: 1);

        Assert.Equal(6, output[0]);
    }
}
