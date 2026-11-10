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
    public void Test_Managed_AssignmentBeforeShuffle_ShuffleReadsTheAssignedValue() =>
        EvaluationOrderSplitTests.AssertAssignmentBeforeShuffle();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_LocalAssignmentBeforeShuffle_ShuffleReadsTheAssignedValue() =>
        EvaluationOrderSplitTests.AssertLocalAssignmentBeforeShuffle();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_CallBeforeShuffle_ShuffleReadsWhatTheCallWrote() =>
        EvaluationOrderSplitTests.AssertCallBeforeShuffle();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_AssignmentBeforeSuspendingCall_CallReadsTheAssignedValue() =>
        EvaluationOrderSplitTests.AssertAssignmentBeforeSuspendingCall();
}
#endif
