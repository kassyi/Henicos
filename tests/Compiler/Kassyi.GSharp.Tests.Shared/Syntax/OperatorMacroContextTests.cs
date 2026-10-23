namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A <c>[GpuOperatorMacro]</c> target that takes a context receives the caller's variable of that type (#477).</summary>
[Category(TestCategory.Syntax)]
public class OperatorMacroContextTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ParameterContext_IsPassedByReference()
    {
        uint[] result = [0];
        uint[] calls = [0];

        OperatorMacroContextKernels.LaunchParameterContextKernel(
            new OperatorMacroContextState { Offset = 100 },
            [3],
            [4],
            result,
            calls
        );

        Assert.Equal(107u, result[0]);
        Assert.Equal(1u, calls[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ParameterContext_IsPassedByValue()
    {
        uint[] result = [0];

        OperatorMacroContextKernels.LaunchValueContextKernel(new OperatorMacroContextState { Offset = 1 }, [5], result);

        Assert.Equal(41u, result[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefParameterContext_OfDeviceFunction_IsPassedOn()
    {
        uint[] result = [0];
        uint[] calls = [0];

        OperatorMacroContextKernels.LaunchRefParameterContextKernel(
            new OperatorMacroContextState { Offset = 10 },
            [1],
            [2],
            result,
            calls
        );

        // (1 + 2 + 10) + 2 + 10
        Assert.Equal(25u, result[0]);
        Assert.Equal(2u, calls[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalContext_AcrossBarrier_IsPassed()
    {
        uint[] result = [0];
        uint[] calls = [0];

        OperatorMacroContextKernels.LaunchHoistedContextKernel([20], [5], [6], result, calls);

        Assert.Equal(31u, result[0]);
        Assert.Equal(1u, calls[0]);
    }
}
