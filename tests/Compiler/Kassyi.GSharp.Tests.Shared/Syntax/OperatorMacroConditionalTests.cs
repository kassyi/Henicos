using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>&amp;&amp;</c> and <c>||</c> over operator macros on the GPU and the native CPU build
/// (<c>OperatorMacroConditionalManagedTests</c>: the managed fallback). <c>a &amp;&amp; b</c> is
/// <c>T.false(a) ? a : T.&amp;(a, b)</c> and <c>a || b</c> is <c>T.true(a) ? a : T.|(a, b)</c> (C# spec §12.14.3):
/// <c>a</c> is evaluated once, and <c>b</c> only when the test fails. <c>true(v)</c> is <c>v &gt; 0</c>,
/// <c>false(v)</c> is <c>v &lt; 0</c>, <c>And(l, r) = 10 l + r</c> and <c>Or(l, r) = 100 l + r</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class OperatorMacroConditionalTests : TranspilerTestBase
{
    // Per thread: the left and right values, then the result and the count (100 per left, 1 per right evaluation)
    internal static readonly int[] AndInput = [-1, 5, 0, 5, 1, 5];

    // false(-1): the result is the left operand and the right one is not evaluated; false(0) and false(1) fail
    internal static readonly int[] AndExpected = [-1, 100, 5, 101, 15, 101];

    internal static readonly int[] OrInput = [1, 5, 0, 5, -1, 5];

    // true(1): the result is the left operand; true(0) and true(-1) fail
    internal static readonly int[] OrExpected = [1, 100, 5, 101, -95, 101];

    internal static readonly int[] NestedInput = [-1, 5, 0, 5, 1, 5];

    // Per thread: c = a && (b & 2), d = (a | 2) || b, e = (a && b) | 2, and how often b was evaluated
    internal static readonly int[] NestedExpected = [-1, -9795, -98, 1, 52, 2, 502, 2, 62, 102, 1502, 2];

    internal static readonly int[] ChainInput = [-1, -1, 1, -1, 1, 1, 0, 0];

    // Per thread: c = a && b && 3, d = a || b && 4, and how often 3 and 4 were evaluated
    internal static readonly int[] ChainExpected = [-1, -101, 0, 93, 1, 1, 113, 1, 1, 3, 4, 2];

    internal static readonly int[] TargetInput = [-1, 5, 1, 5];

    // values[2 * i] is the result, values[2 * i + 1] keeps 0
    internal static readonly int[] TargetExpected = [-1, 0, 15, 0];

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConditionalAnd_TestsOperatorFalseBeforeTheOperatorMacro()
    {
        var result = new int[AndExpected.Length];

        OperatorMacroConditionalKernels.LaunchAndKernel(AndInput, result, blockDimX: 3);

        Assert.Equal(AndExpected, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConditionalOr_TestsOperatorTrueBeforeTheOperatorMacro()
    {
        var result = new int[OrExpected.Length];

        OperatorMacroConditionalKernels.LaunchOrKernel(OrInput, result, blockDimX: 3);

        Assert.Equal(OrExpected, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConditionalOperatorWithNestedOperatorMacros_EvaluatesTheRightOperandOnlyWhenTheTestFails()
    {
        var result = new int[NestedExpected.Length];

        OperatorMacroConditionalKernels.LaunchNestedKernel(NestedInput, result, blockDimX: 3);

        Assert.Equal(NestedExpected, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ChainedConditionalOperators_ShortCircuitEachLevel()
    {
        var result = new int[ChainExpected.Length];

        OperatorMacroConditionalKernels.LaunchChainKernel(ChainInput, result, blockDimX: 4);

        Assert.Equal(ChainExpected, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConditionalOperatorTarget_IsEvaluatedBeforeTheLeftOperand()
    {
        var values = new TruthValue[4];
        var index = new int[2];

        OperatorMacroConditionalKernels.LaunchTargetOrderKernel(TargetInput, values, index, blockDimX: 2);

        Assert.Equal(TargetExpected, new[] { values[0].Value, values[1].Value, values[2].Value, values[3].Value });
        Assert.Equal([1, 1], index);
    }
}
