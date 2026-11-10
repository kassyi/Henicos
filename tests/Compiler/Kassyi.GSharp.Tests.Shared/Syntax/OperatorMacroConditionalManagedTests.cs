#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>&amp;&amp;</c> and <c>||</c> over operator macros on the managed fallback, which calls <c>operator false</c> and
/// <c>operator true</c> before the target of the operator macro. The values are those of
/// <see cref="OperatorMacroConditionalTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroConditionalManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedConditionalAnd_TestsOperatorFalseBeforeOperatorMacro()
    {
        var result = new int[OperatorMacroConditionalTests.AndExpected.Length];

        ManagedKernelRun.Run(() => OperatorMacroConditionalKernels.LaunchAndKernel(
            OperatorMacroConditionalTests.AndInput, result, blockDimX: 3));

        Assert.Equal(OperatorMacroConditionalTests.AndExpected, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_ManagedConditionalOr_TestsOperatorTrueBeforeOperatorMacro()
    {
        var result = new int[OperatorMacroConditionalTests.OrExpected.Length];

        ManagedKernelRun.Run(() => OperatorMacroConditionalKernels.LaunchOrKernel(
            OperatorMacroConditionalTests.OrInput, result, blockDimX: 3));

        Assert.Equal(OperatorMacroConditionalTests.OrExpected, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedConditionalOperatorWithNestedOperatorMacros_EvaluatesRightOperandOnlyWhenTestFails()
    {
        var result = new int[OperatorMacroConditionalTests.NestedExpected.Length];

        ManagedKernelRun.Run(() => OperatorMacroConditionalKernels.LaunchNestedKernel(
            OperatorMacroConditionalTests.NestedInput, result, blockDimX: 3));

        Assert.Equal(OperatorMacroConditionalTests.NestedExpected, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedChainedConditionalOperators_ShortCircuitsEachLevel()
    {
        var result = new int[OperatorMacroConditionalTests.ChainExpected.Length];

        ManagedKernelRun.Run(() => OperatorMacroConditionalKernels.LaunchChainKernel(
            OperatorMacroConditionalTests.ChainInput, result, blockDimX: 4));

        Assert.Equal(OperatorMacroConditionalTests.ChainExpected, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedConditionalOperatorTarget_IsEvaluatedBeforeLeftOperand()
    {
        var values = new TruthValue[4];
        var index = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroConditionalKernels.LaunchTargetOrderKernel(
            OperatorMacroConditionalTests.TargetInput, values, index, blockDimX: 2));

        Assert.Equal(OperatorMacroConditionalTests.TargetExpected,
            new[] { values[0].Value, values[1].Value, values[2].Value, values[3].Value });
        Assert.Equal([1, 1], index);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Expand_ManagedConditionalOperatorsOnRefStruct_TestsLeftOperand()
    {
        var result = new int[OperatorMacroConditionalTests.RefStructExpected.Length];

        ManagedKernelRun.Run(() => OperatorMacroConditionalKernels.LaunchRefStructKernel(
            OperatorMacroConditionalTests.RefStructInput, result, blockDimX: 3));

        Assert.Equal(OperatorMacroConditionalTests.RefStructExpected, result);
    }
}
#endif
