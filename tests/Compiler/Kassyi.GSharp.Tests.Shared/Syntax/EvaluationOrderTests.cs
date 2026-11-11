using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The C++ that the transpiler emits evaluates operands in the C# order (#608), on the native dialects: MSVC
/// evaluates function arguments from right to left, and C++17 evaluates the right side of an assignment first.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class EvaluationOrderTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CallArguments_WithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.CallArguments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void VectorConstructorArguments_WithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.VectorArguments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CallArguments_WithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillCalls(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingCallArguments_WithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.SuspendingCallArguments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void OperatorMacroOperands_WithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.OperatorMacro(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void OperatorMacroOperands_WithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillOperatorMacro(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RefReadsAndAliases_WithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.ReadsAndAliases(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RefReturningCalls_WithAliases_KeepTheirTargetAndAliases() =>
        EvaluationOrderScenarios.RefReturn(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedValues_CSharpConversions_KeepExpectedConversions() =>
        EvaluationOrderScenarios.Conversions(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(0)]
    public void InterferingOperandsInBranch_SelectedBranch_RunOnlyInThatBranch(int selector) =>
        EvaluationOrderScenarios.ConditionalBranches(s_runNative, selector);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Assignment_WithInterference_EvaluatesTargetBeforeValue() =>
        EvaluationOrderScenarios.Assignment(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CompoundAssignment_WithInterference_ReadsTargetBeforeValue() =>
        EvaluationOrderScenarios.CompoundAssignment(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Assignments_WithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillAssignments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void BinaryOperands_WithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.BinaryOperator(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WritesHiddenInCallee_ArgumentOrder_KeepArgumentOrder() =>
        EvaluationOrderScenarios.HiddenWrites(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void BinaryOperands_WithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillBinary(s_runNative);
}
