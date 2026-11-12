#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

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
    public void CallArguments_ManagedExecutionWithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.CallArguments(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void VectorConstructorArguments_ManagedExecutionWithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.VectorArguments(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingCallArguments_ManagedExecutionWithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.SuspendingCallArguments(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RefReadsAndAliases_ManagedExecutionWithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.ReadsAndAliases(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RefReturningCalls_ManagedExecutionWithAliases_KeepTheirTargetAndAliases() =>
        EvaluationOrderScenarios.RefReturn(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedValues_ManagedExecutionConversions_KeepExpectedConversions() =>
        EvaluationOrderScenarios.Conversions(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(0)]
    public void InterferingOperandsInBranch_ManagedExecutionSelectedBranch_RunOnlyInThatBranch(int selector) =>
        EvaluationOrderScenarios.ConditionalBranches(s_runManaged, selector);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Assignment_ManagedExecutionWithInterference_EvaluatesTargetBeforeValue() =>
        EvaluationOrderScenarios.Assignment(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompoundAssignment_ManagedExecutionWithInterference_ReadsTargetBeforeValue() =>
        EvaluationOrderScenarios.CompoundAssignment(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BinaryOperands_ManagedExecutionWithInterference_AreEvaluatedFromLeftToRight() =>
        EvaluationOrderScenarios.BinaryOperator(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void WritesHiddenInCallee_ManagedExecutionArgumentOrder_KeepArgumentOrder() =>
        EvaluationOrderScenarios.HiddenWrites(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
