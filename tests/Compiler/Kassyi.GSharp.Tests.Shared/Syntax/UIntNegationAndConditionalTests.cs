using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The C++ that the transpiler emits negates a uint as a long and converts both branches of a target-typed conditional
/// to its target type, as C# does (#671), on the GPU and the native CPU build.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class UIntNegationAndConditionalTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UIntNegation_IsComputedInLong() =>
        UIntNegationAndConditionalScenarios.Negation(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TargetTypedConditional_ConvertsBothBranches() =>
        UIntNegationAndConditionalScenarios.Conditional(s_runNative);
}
