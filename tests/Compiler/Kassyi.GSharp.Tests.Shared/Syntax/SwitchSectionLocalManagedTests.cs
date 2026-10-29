#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

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
    public void Test_Managed_InitializedInFirstSection_AssignedInLaterSection(int flag) =>
        SwitchSectionLocalScenarios.InitializedInFirstSection(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SharedStructAndVar_EverySectionSeesTheLocals() =>
        SwitchSectionLocalScenarios.SharedStructAndVar(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GotoCaseAndNestedSwitch_LocalCarriedIntoNextSection() =>
        SwitchSectionLocalScenarios.GotoCaseAndNestedSwitch(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_SectionConstant_LaterSectionReadsTheValue(int flag) =>
        SwitchSectionLocalScenarios.SectionConstant(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_GotoPastArrayAlias_SkipsTheDeclaration(int flag) =>
        SwitchSectionLocalScenarios.GotoPastArrayAlias(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_GotoInSharedSection_DeclaredOnce(int flag) =>
        SwitchSectionLocalScenarios.GotoInSharedSection(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_ByteConstant_KeepsItsTypeInAnotherSection(int flag) =>
        SwitchSectionLocalScenarios.ByteConstant(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_ConstantInArgument_PassedByReference(int flag) =>
        SwitchSectionLocalScenarios.ConstantInArgument(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_ArrayFieldStruct_SharedBySections(int flag) =>
        SwitchSectionLocalScenarios.ArrayFieldStruct(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_GotoPastArrayFieldStruct_SkipsTheDeclaration(int flag) =>
        SwitchSectionLocalScenarios.GotoPastArrayFieldStruct(s_runManaged, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_ShuffleBeforeDeclaration_LocalSharedAfterLowering(int flag) =>
        SwitchSectionLocalScenarios.ShuffleBeforeDeclaration(s_runManaged, flag);

    private static void RunManaged(Action launch)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
}
#endif
