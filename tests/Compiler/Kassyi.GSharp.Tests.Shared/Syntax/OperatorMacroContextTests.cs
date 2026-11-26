namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A <c>[GpuOperatorMacro]</c> target that takes a context receives the caller's variable of that type (#477).</summary>
[Category(TestCategory.Syntax)]
public class OperatorMacroContextTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ParameterContext_IsPassedByReference()
    {
        uint[] result = [0];
        uint[] calls = [0];

        OperatorMacroContextKernels.LaunchParameterContextKernel(
            bigCtx: new OperatorMacroContextState { Offset = 100 },
            operands: new OperatorMacroBinaryOperands { Left = [3], Right = [4] },
            result: result,
            calls: calls
        );

        Assert.Equal(107u, result[0]);
        Assert.Equal(1u, calls[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ParameterContext_IsPassedByValue()
    {
        uint[] result = [0];

        OperatorMacroContextKernels.LaunchValueContextKernel(new OperatorMacroContextState { Offset = 1 }, [5], result);

        Assert.Equal(41u, result[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Expand_RefParameterContextOfDeviceFunction_IsPassedOn()
    {
        uint[] result = [0];
        uint[] calls = [0];

        OperatorMacroContextKernels.LaunchRefParameterContextKernel(
            bigCtx: new OperatorMacroContextState { Offset = 10 },
            operands: new OperatorMacroBinaryOperands { Left = [1], Right = [2] },
            result: result,
            calls: calls
        );

        // (1 + 2 + 10) + 2 + 10
        Assert.Equal(25u, result[0]);
        Assert.Equal(2u, calls[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Expand_LocalContextAcrossBarrier_IsPassed()
    {
        uint[] result = [0];
        uint[] calls = [0];

        OperatorMacroContextKernels.LaunchHoistedContextKernel(
            offset: [20],
            operands: new OperatorMacroBinaryOperands { Left = [5], Right = [6] },
            result: result,
            calls: calls);

        Assert.Equal(31u, result[0]);
        Assert.Equal(1u, calls[0]);
    }
}
