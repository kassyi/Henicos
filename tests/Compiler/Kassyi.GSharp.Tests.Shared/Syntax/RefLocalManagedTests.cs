#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback of <see cref="RefLocalTests"/>.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class RefLocalManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AliasOfLocal_Managed_ChangesTheLocal() =>
        RefLocalScenarios.AliasOfLocal(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AliasOfElement_Managed_ChangesTheElement() =>
        RefLocalScenarios.AliasOfElement(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Rebind_Managed_BindsTheLocalToAnotherVariable() =>
        RefLocalScenarios.Rebind(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, new[] { 9, 2 })]
    [InlineData(0, new[] { 1, 9 })]
    public void Conditional_Managed_BindsTheChosenVariable(int c, int[] expected) =>
        RefLocalScenarios.Conditional(s_runManaged, c, expected);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RefReadOnly_Managed_SeesTheValueWrittenThroughAnotherPath() =>
        RefLocalScenarios.RefReadOnly(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ForEachRef_ManagedSpan_ChangesTheElementOfEachThread() =>
        RefLocalScenarios.ForEachRefSpan(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ForEachRef_ManagedArrayWithContinueAndBreak_ChangesTheVisitedElements() =>
        RefLocalScenarios.ForEachRefArray(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 5)]
    public void GotoPastRefLocal_Managed_SkipsTheDeclaration(int flag, int expectedFirst) =>
        RefLocalScenarios.GotoPastRefLocal(s_runManaged, flag, expectedFirst);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
