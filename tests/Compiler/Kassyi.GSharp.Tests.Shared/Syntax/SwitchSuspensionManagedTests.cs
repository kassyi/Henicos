#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback must split at a barrier or a warp shuffle inside a switch section too (#708).</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SwitchSuspensionManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSuspension_ManagedBarrierInSection_ReadAfterSwitchSeesEveryWrite(int k) =>
        SwitchSuspensionScenarios.BarrierInSection(s_runManaged, k);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SwitchSuspension_ManagedShuffleInSeveralSections_EachSectionReadsItsLane(int flag) =>
        SwitchSuspensionScenarios.ShuffleInSeveralSections(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void SwitchSuspension_ManagedNestedSwitch_ShuffleInInnerAndOuterSections(int outer, int inner) =>
        SwitchSuspensionScenarios.NestedSwitch(s_runManaged, outer, inner);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SwitchSuspension_ManagedGotoCase_JumpsBetweenSectionsThatShuffle(int flag) =>
        SwitchSuspensionScenarios.GotoCase(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void SwitchSuspension_ManagedSwitchInLoop_BreakLeavesSwitchContinueNextIteration(int rounds) =>
        SwitchSuspensionScenarios.SwitchInLoop(s_runManaged, rounds);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void SwitchSuspension_ManagedGoverningEvaluatedOnce_SideEffectRunsOnce(int flag) =>
        SwitchSuspensionScenarios.GoverningEvaluatedOnce(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSuspension_ManagedSectionConstantAcrossBarrier_OtherSectionReadsItsValue(int flag) =>
        SwitchSuspensionScenarios.SectionConstantAcrossBarrier(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SwitchSuspension_ManagedSectionConstantCaseLabel_DispatchMatchesItsValue(int flag) =>
        SwitchSuspensionScenarios.SectionConstantCaseLabel(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SwitchSuspension_ManagedSectionConstantChain_LaterConstReadsEarlierOne(int flag) =>
        SwitchSuspensionScenarios.SectionConstantChain(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSuspension_ManagedGotoCaseChar_MatchesCaseByValue(int flag) =>
        SwitchSuspensionScenarios.GotoCaseChar(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSuspension_ManagedGotoCaseEnum_MatchesCaseByValue(int flag) =>
        SwitchSuspensionScenarios.GotoCaseEnum(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(float.NaN)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void SwitchSuspension_ManagedNaNCase_MatchesNaN(float selector) =>
        SwitchSuspensionScenarios.NaNCase(s_runManaged, selector);

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
