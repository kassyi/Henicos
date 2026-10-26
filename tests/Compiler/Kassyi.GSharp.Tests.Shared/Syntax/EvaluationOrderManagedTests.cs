#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed C# fallback runs the C# itself, or the lowered state machine of a kernel that suspends, so it must
/// match the C# order as well (#608).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class EvaluationOrderManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_CallArguments_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.CallArguments(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_VectorConstructorArguments_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.VectorArguments(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SuspendingCallArguments_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.SuspendingCallArguments(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_RefReadsAndAliases_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.ReadsAndAliases(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_RefReturningCalls_KeepTheirTargetAndAliases() =>
        EvaluationOrderScenarios.RefReturn(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ConvertedValues_KeepTheCSharpConversions() =>
        EvaluationOrderScenarios.Conversions(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(0)]
    public void Test_Managed_InterferingOperandsInABranch_RunOnlyInThatBranch(int selector) =>
        EvaluationOrderScenarios.ConditionalBranches(s_runManaged, selector);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Assignment_EvaluatesTheTargetBeforeTheValue() =>
        EvaluationOrderScenarios.Assignment(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_CompoundAssignment_ReadsTheTargetBeforeTheValue() =>
        EvaluationOrderScenarios.CompoundAssignment(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_BinaryOperands_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.BinaryOperator(s_runManaged);

    private static void RunManaged(Action launch)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
}
#endif
