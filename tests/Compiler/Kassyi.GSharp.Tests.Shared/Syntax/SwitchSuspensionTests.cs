using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A barrier or a warp shuffle inside a switch section on the native dialects (#708).</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SwitchSuspensionTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_BarrierInSection_ReadAfterSwitchSeesEveryWrite(int k) =>
        SwitchSuspensionScenarios.BarrierInSection(s_runNative, k);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Test_ShuffleInSeveralSections_EachSectionReadsItsLane(int flag) =>
        SwitchSuspensionScenarios.ShuffleInSeveralSections(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void Test_NestedSwitch_ShuffleInInnerAndOuterSections(int outer, int inner) =>
        SwitchSuspensionScenarios.NestedSwitch(s_runNative, outer, inner);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Test_GotoCase_JumpsBetweenSectionsThatShuffle(int flag) =>
        SwitchSuspensionScenarios.GotoCase(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void Test_SwitchInLoop_BreakLeavesSwitchContinueNextIteration(int rounds) =>
        SwitchSuspensionScenarios.SwitchInLoop(s_runNative, rounds);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void Test_GoverningEvaluatedOnce_SideEffectRunsOnce(int flag) =>
        SwitchSuspensionScenarios.GoverningEvaluatedOnce(s_runNative, flag);
}
