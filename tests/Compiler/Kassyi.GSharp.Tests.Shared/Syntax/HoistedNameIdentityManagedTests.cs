#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Managed state-machine execution counterparts of <see cref="HoistedNameIdentityTests"/>.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class HoistedNameIdentityManagedTests : IDisposable
{
    private readonly bool _previousForceManaged;

    public HoistedNameIdentityManagedTests()
    {
        _previousForceManaged = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
    }

    public void Dispose()
    {
        CpuKernelLoader.ForceManagedExecution = _previousForceManaged;
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BarrierLocalSameNameInitializerKernel_PreservesMemberAndLocal() =>
        HoistedNameIdentityTests.AssertInitializerSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BarrierLocalSameNameWithKernel_PreservesMemberAndLocal() =>
        HoistedNameIdentityTests.AssertWithSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BarrierLocalSameNameMemberAccessKernel_PreservesMemberAndLocal() =>
        HoistedNameIdentityTests.AssertMemberAccessSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BarrierLocalSameNameLambdaParameterKernel_DoesNotCaptureHoistedLocal() =>
        HoistedNameIdentityTests.AssertLambdaParameterSameName();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SiblingBlockSameLabelKernel_BranchesToEachBlockLabel() =>
        HoistedNameIdentityTests.AssertSiblingBlockSameLabel();
}
#endif
