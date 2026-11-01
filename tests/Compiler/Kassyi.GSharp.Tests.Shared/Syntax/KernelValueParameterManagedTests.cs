#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Managed state-machine execution counterparts of <see cref="KernelValueParameterTests"/>.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class KernelValueParameterManagedTests : IDisposable
{
    private readonly bool _previousForceManaged;

    public KernelValueParameterManagedTests()
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
    public void Test_Managed_ScalarParameterAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertScalarParameter();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ArrayParameterAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertArrayParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ReadOnlyArrayParameterAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertReadOnlyArrayParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ArrayParameterAssignedFromStructField_SurvivesBarrier() =>
        KernelValueParameterTests.AssertArrayFromStructField();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_StructParameterAssignment_SurvivesBarrier() =>
        KernelValueParameterTests.AssertStructParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StructParameterFieldWrite_SurvivesBarrier() =>
        KernelValueParameterTests.AssertStructFieldParameter();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ParameterWrittenAcrossTwoBarriers_KeepsEveryWrite() =>
        KernelValueParameterTests.AssertAcrossTwoBarriers();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ParameterWrittenInBarrierLoop_KeepsEveryIteration() =>
        KernelValueParameterTests.AssertInBarrierLoop();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RefExtensionReceiver_SurvivesBarrier() =>
        KernelValueParameterTests.AssertRefExtensionReceiver();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RefArgumentAndIncrement_SurvivesBarrier() =>
        KernelValueParameterTests.AssertRefArgumentAndIncrement();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OutArgument_SurvivesBarrier() =>
        KernelValueParameterTests.AssertOutArgument();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_Deconstruction_SurvivesBarrier() =>
        KernelValueParameterTests.AssertDeconstruction();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_LocalFunctionWrite_SurvivesBarrier() =>
        KernelValueParameterTests.AssertLocalFunctionWrite();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ParameterWrittenWithoutBarrier_KeepsWrite() =>
        KernelValueParameterTests.AssertWithoutBarrier();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RefTernaryParameterWrite_SurvivesBarrier() =>
        KernelValueParameterTests.AssertRefTernaryParameter();
}
#endif
