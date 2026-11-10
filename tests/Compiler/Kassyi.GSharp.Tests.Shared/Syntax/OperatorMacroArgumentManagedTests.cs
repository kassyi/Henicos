#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroArgumentKernels"/> on the managed fallback (#711). The values are those of
/// <see cref="OperatorMacroArgumentTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroArgumentManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedInOperand_IsPassed()
    {
        var result = new int[1];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchInArgumentKernel(result, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.InArgument, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedInOperandBeforeInnerWrite_IsReadThroughItsVariable()
    {
        InValue[] values = [new InValue(3), new InValue(5)];
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchInSpillKernel(values, result, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.InSpill, new[] { result[0], result[1], values[0].V, values[1].V });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedRefResultBeforeInnerCall_IsElementChosenFirst()
    {
        var values = new SpillValue[2];
        var calls = new int[1];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchRefSpillKernel(values, calls, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.RefSpill, new[] { values[0].V, values[1].V, calls[0] });
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedOutResult_IsAssignedByTarget()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchOutResultKernel(result, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.OutResult, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedInOperandReturnedByReferenceBeforeInnerWrite_IsReadThroughReference()
    {
        InValue[] values = [new InValue(3), new InValue(5)];
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchRefCallSpillKernel(values, result, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.InSpill, new[] { result[0], result[1], values[0].V, values[1].V });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedSharedMemoryInOperandBeforeInnerWrite_IsReadThroughElement()
    {
        var result = new int[3];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchSharedRefSpillKernel(result, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.SharedSpill, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedAutoPropertyTargetAndOperand_UsesStorage()
    {
        var result = new int[5];

        ManagedKernelRun.Run(() => OperatorMacroArgumentKernels.LaunchPropertyTargetKernel(result, blockDimX: 1));

        Assert.Equal(OperatorMacroArgumentCases.PropertyTarget, result);
    }

}
#endif
