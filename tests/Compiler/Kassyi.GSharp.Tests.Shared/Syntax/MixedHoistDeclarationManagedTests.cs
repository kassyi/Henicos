#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Managed state-machine execution counterparts of <see cref="MixedHoistDeclarationTests"/>.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class MixedHoistDeclarationManagedTests : IDisposable
{
    private readonly bool _previousForceManaged;

    public MixedHoistDeclarationManagedTests()
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
    public void Test_Managed_HoistedBeforeLocalKernel_LaterDeclaratorReadsHoistedValue() =>
        MixedHoistDeclarationTests.AssertHoistedBeforeLocal();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SideEffectOrderKernel_DeclaratorsRunInSourceOrder() =>
        MixedHoistDeclarationTests.AssertSideEffectOrder();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_AlternatingHoistKernel_DeclaratorsRunInSourceOrder() =>
        MixedHoistDeclarationTests.AssertAlternatingHoist();
}
#endif
