using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroContextKernels"/> on the managed fallback, which expands each operator macro
/// into the C# call of its target (#711). The values are those of <see cref="OperatorMacroContextTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroContextManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedParameterContext_IsPassedByReference()
    {
        uint[] result = [0];
        uint[] calls = [0];

        ManagedKernelRun.Run(() => OperatorMacroContextKernels.LaunchParameterContextKernel(
            bigCtx: new OperatorMacroContextState { Offset = 100 },
            operands: new OperatorMacroBinaryOperands { Left = [3], Right = [4] },
            result: result,
            calls: calls
        ));

        Assert.Equal(107u, result[0]);
        Assert.Equal(1u, calls[0]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedParameterContext_IsPassedByValue()
    {
        uint[] result = [0];

        ManagedKernelRun.Run(() =>
            OperatorMacroContextKernels.LaunchValueContextKernel(new OperatorMacroContextState { Offset = 1 }, [5], result));

        Assert.Equal(41u, result[0]);
    }

    // AddTwice is a device function that expands the operator macro: the kernel calls its managed version
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedRefParameterContextOfDeviceFunction_IsPassedOn()
    {
        uint[] result = [0];
        uint[] calls = [0];

        ManagedKernelRun.Run(() => OperatorMacroContextKernels.LaunchRefParameterContextKernel(
            bigCtx: new OperatorMacroContextState { Offset = 10 },
            operands: new OperatorMacroBinaryOperands { Left = [1], Right = [2] },
            result: result,
            calls: calls
        ));

        // (1 + 2 + 10) + 2 + 10
        Assert.Equal(25u, result[0]);
        Assert.Equal(2u, calls[0]);
    }

    // The context is a field of the kernel's state machine context: the expansion passes that field
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedLocalContextAcrossBarrier_IsPassed()
    {
        uint[] result = [0];
        uint[] calls = [0];

        ManagedKernelRun.Run(() => OperatorMacroContextKernels.LaunchHoistedContextKernel(
            offset: [20],
            operands: new OperatorMacroBinaryOperands { Left = [5], Right = [6] },
            result: result,
            calls: calls));

        Assert.Equal(31u, result[0]);
        Assert.Equal(1u, calls[0]);
    }
}
