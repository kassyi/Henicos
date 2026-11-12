#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback must give the same results as the native dialects for shared switch locals.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SwitchSectionLocalManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedInitializedInFirstSection_AssignedInLaterSection(int flag) =>
        SwitchSectionLocalScenarios.InitializedInFirstSection(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SwitchSectionLocal_ManagedSharedStructAndVar_EverySectionSeesTheLocals() =>
        SwitchSectionLocalScenarios.SharedStructAndVar(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SwitchSectionLocal_ManagedGotoCaseAndNestedSwitch_LocalCarriedIntoNextSection() =>
        SwitchSectionLocalScenarios.GotoCaseAndNestedSwitch(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedSectionConstant_LaterSectionReadsTheValue(int flag) =>
        SwitchSectionLocalScenarios.SectionConstant(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedGotoPastArrayAlias_SkipsTheDeclaration(int flag) =>
        SwitchSectionLocalScenarios.GotoPastArrayAlias(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedGotoInSharedSection_DeclaredOnce(int flag) =>
        SwitchSectionLocalScenarios.GotoInSharedSection(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedByteConstant_KeepsItsTypeInAnotherSection(int flag) =>
        SwitchSectionLocalScenarios.ByteConstant(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedConstantInArgument_PassedByReference(int flag) =>
        SwitchSectionLocalScenarios.ConstantInArgument(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedArrayFieldStruct_SharedBySections(int flag) =>
        SwitchSectionLocalScenarios.ArrayFieldStruct(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedGotoPastArrayFieldStruct_SkipsTheDeclaration(int flag) =>
        SwitchSectionLocalScenarios.GotoPastArrayFieldStruct(s_runManaged, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionLocal_ManagedShuffleBeforeDeclaration_LocalSharedAfterLowering(int flag) =>
        SwitchSectionLocalScenarios.ShuffleBeforeDeclaration(s_runManaged, flag);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
