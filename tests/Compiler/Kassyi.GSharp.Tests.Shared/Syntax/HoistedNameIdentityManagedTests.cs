#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Managed state-machine execution counterparts of <see cref="HoistedNameIdentityTests"/>.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class HoistedNameIdentityManagedTests : IDisposable
{
    private readonly GpuExecutionScope _managedScope;

    public HoistedNameIdentityManagedTests()
    {
        _managedScope = GpuExecution.Use(KernelExecution.Managed);
    }

    public void Dispose()
    {
        _managedScope.Dispose();
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteManaged_BarrierLocalSameNameInitializer_PreservesMemberAndLocal() =>
        HoistedNameIdentityTests.AssertInitializerSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteManaged_BarrierLocalSameNameWith_PreservesMemberAndLocal() =>
        HoistedNameIdentityTests.AssertWithSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteManaged_BarrierLocalSameNameMemberAccess_PreservesMemberAndLocal() =>
        HoistedNameIdentityTests.AssertMemberAccessSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteManaged_BarrierLocalSameNameLambdaParameter_DoesNotCaptureHoistedLocal() =>
        HoistedNameIdentityTests.AssertLambdaParameterSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteManaged_SiblingBlockSameLabel_BranchesToEachBlockLabel() =>
        HoistedNameIdentityTests.AssertSiblingBlockSameLabel();
}
#endif
