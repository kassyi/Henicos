#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Managed state-machine execution counterparts of <see cref="MixedHoistDeclarationTests"/>.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class MixedHoistDeclarationManagedTests : IDisposable
{
    private readonly GpuExecutionScope _managedScope;

    public MixedHoistDeclarationManagedTests()
    {
        _managedScope = GpuExecution.Use(KernelExecution.Managed);
    }

    public void Dispose()
    {
        _managedScope.Dispose();
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HoistDeclaration_ManagedHoistedBeforeLocal_LaterDeclaratorReadsHoistedValue() =>
        MixedHoistDeclarationTests.AssertHoistedBeforeLocal();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HoistDeclaration_ManagedSideEffectOrder_DeclaratorsRunInSourceOrder() =>
        MixedHoistDeclarationTests.AssertSideEffectOrder();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HoistDeclaration_ManagedAlternatingHoist_DeclaratorsRunInSourceOrder() =>
        MixedHoistDeclarationTests.AssertAlternatingHoist();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HoistDeclaration_ManagedLabeledHoistedBeforeLocal_LaterDeclaratorReadsHoistedValue() =>
        MixedHoistDeclarationTests.AssertLabeledHoistedBeforeLocal();
}
#endif
