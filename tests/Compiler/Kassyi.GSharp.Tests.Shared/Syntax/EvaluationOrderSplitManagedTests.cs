#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Managed state-machine execution counterparts of <see cref="EvaluationOrderSplitTests"/>.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class EvaluationOrderSplitManagedTests : IDisposable
{
    private readonly bool _previousForceManaged;

    public EvaluationOrderSplitManagedTests()
    {
        _previousForceManaged = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
    }

    public void Dispose()
    {
        CpuKernelLoader.ForceManagedExecution = _previousForceManaged;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AssignmentBeforeShuffle_ManagedExecution_ShuffleReadsAssignedValue() =>
        EvaluationOrderSplitTests.AssertAssignmentBeforeShuffle();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalAssignmentBeforeShuffle_ManagedExecution_ShuffleReadsAssignedValue() =>
        EvaluationOrderSplitTests.AssertLocalAssignmentBeforeShuffle();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CallBeforeShuffle_ManagedExecution_ShuffleReadsWhatTheCallWrote() =>
        EvaluationOrderSplitTests.AssertCallBeforeShuffle();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AssignmentBeforeSuspendingCall_ManagedExecution_CallReadsAssignedValue() =>
        EvaluationOrderSplitTests.AssertAssignmentBeforeSuspendingCall();
}
#endif
