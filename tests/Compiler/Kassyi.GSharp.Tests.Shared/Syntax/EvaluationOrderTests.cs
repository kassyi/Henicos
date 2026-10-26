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
    public void Test_CallArguments_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.CallArguments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_VectorConstructorArguments_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.VectorArguments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CallArgumentsWithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillCalls(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SuspendingCallArguments_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.SuspendingCallArguments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OperatorMacroOperands_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.OperatorMacro(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorMacroOperandsWithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillOperatorMacro(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Assignment_EvaluatesTheTargetBeforeTheValue() =>
        EvaluationOrderScenarios.Assignment(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompoundAssignment_ReadsTheTargetBeforeTheValue() =>
        EvaluationOrderScenarios.CompoundAssignment(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AssignmentsWithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillAssignments(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BinaryOperands_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.BinaryOperator(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BinaryOperandsWithoutInterference_KeepTheirValues() =>
        EvaluationOrderScenarios.NoSpillBinary(s_runNative);
}
