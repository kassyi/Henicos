#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Managed state-machine execution counterparts of <see cref="SuspendingValueParameterTests"/>.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SuspendingValueParameterManagedTests : IDisposable
{
    private readonly bool _previousForceManaged;

    public SuspendingValueParameterManagedTests()
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
    public void SuspendingValueParameter_ManagedStructFieldWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertStructFieldWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedNestedFieldWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertNestedFieldWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedRefExtensionReceiverWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertRefExtensionWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedInlineArrayElementWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertInlineArrayElementWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedPrefixIncrementOfField_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertPrefixIncrementField();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedFieldPassedByRef_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertRefFieldArgument();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedArrayElementWrite_IsVisibleAfterBarrierAndToCaller() =>
        SuspendingValueParameterTests.AssertArrayElementWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedDeconstructingSwapOfFields_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertTupleSwapFields();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingValueParameter_ManagedDeconstructingSwapOfScalars_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertTupleSwapScalars();
}
#endif
