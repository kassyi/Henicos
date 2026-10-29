using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Locals shared by switch sections on the native dialects (#795).</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SwitchSectionLocalTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_InitializedInFirstSection_AssignedInLaterSection(int flag) =>
        SwitchSectionLocalScenarios.InitializedInFirstSection(s_runNative, flag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SharedStructAndVar_EverySectionSeesTheLocals() =>
        SwitchSectionLocalScenarios.SharedStructAndVar(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GotoCaseAndNestedSwitch_LocalCarriedIntoNextSection() =>
        SwitchSectionLocalScenarios.GotoCaseAndNestedSwitch(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_SectionConstant_LaterSectionReadsTheValue(int flag) =>
        SwitchSectionLocalScenarios.SectionConstant(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_ByteConstant_KeepsItsTypeInAnotherSection(int flag) =>
        SwitchSectionLocalScenarios.ByteConstant(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_ConstantInArgument_PassedByReference(int flag) =>
        SwitchSectionLocalScenarios.ConstantInArgument(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_ArrayFieldStruct_SharedBySections(int flag) =>
        SwitchSectionLocalScenarios.ArrayFieldStruct(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_GotoPastArrayFieldStruct_SkipsTheDeclaration(int flag) =>
        SwitchSectionLocalScenarios.GotoPastArrayFieldStruct(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_ShuffleBeforeDeclaration_LocalSharedAfterLowering(int flag) =>
        SwitchSectionLocalScenarios.ShuffleBeforeDeclaration(s_runNative, flag);
}
