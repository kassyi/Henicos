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
    public void Test_Managed_StructFieldWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertStructFieldWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NestedFieldWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertNestedFieldWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RefExtensionReceiverWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertRefExtensionWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_InlineArrayElementWrite_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertInlineArrayElementWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PrefixIncrementOfField_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertPrefixIncrementField();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_FieldPassedByRef_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertRefFieldArgument();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ArrayElementWrite_IsVisibleAfterBarrierAndToCaller() =>
        SuspendingValueParameterTests.AssertArrayElementWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_DeconstructingSwapOfFields_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertTupleSwapFields();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_DeconstructingSwapOfScalars_SurvivesBarrier() =>
        SuspendingValueParameterTests.AssertTupleSwapScalars();
}
#endif
