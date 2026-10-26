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
