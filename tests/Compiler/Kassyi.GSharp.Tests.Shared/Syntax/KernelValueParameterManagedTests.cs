#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Managed state-machine execution counterparts of <see cref="KernelValueParameterTests"/>.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class KernelValueParameterManagedTests : IDisposable
{
    private readonly GpuExecutionScope _managedScope;

    public KernelValueParameterManagedTests()
    {
        _managedScope = GpuExecution.Use(KernelExecution.Managed);
    }

    public void Dispose()
    {
        _managedScope.Dispose();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Parameter_ManagedScalarAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertScalarParameter();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Parameter_ManagedArrayAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertArrayParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedReadOnlyArrayAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertReadOnlyArrayParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedArrayAssignedFromStructField_SurvivesBarrier() =>
        KernelValueParameterTests.AssertArrayFromStructField();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Parameter_ManagedStructAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertStructParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedStructFieldWrite_SurvivesBarrier() =>
        KernelValueParameterTests.AssertStructFieldParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedWrittenAcrossTwoBarriers_KeepsEveryWrite() =>
        KernelValueParameterTests.AssertAcrossTwoBarriers();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedWrittenInBarrierLoop_KeepsEveryIteration() =>
        KernelValueParameterTests.AssertInBarrierLoop();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedRefExtensionReceiver_SurvivesBarrier() =>
        KernelValueParameterTests.AssertRefExtensionReceiver();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedRefArgumentAndIncrement_SurvivesBarrier() =>
        KernelValueParameterTests.AssertRefArgumentAndIncrement();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedOutArgument_SurvivesBarrier() =>
        KernelValueParameterTests.AssertOutArgument();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedDeconstruction_SurvivesBarrier() =>
        KernelValueParameterTests.AssertDeconstruction();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedLocalFunctionWrite_SurvivesBarrier() =>
        KernelValueParameterTests.AssertLocalFunctionWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Parameter_ManagedWrittenWithoutBarrier_KeepsWrite() =>
        KernelValueParameterTests.AssertWithoutBarrier();
}
#endif
