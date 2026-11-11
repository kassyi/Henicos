using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Labels of the user on the native dialects. CUDA is the reference: the CPU dialect must give the same results.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class UserLabelTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LabeledShuffle_NativeLoopWithGoto_RunsAgainOnEveryGoto() =>
        UserLabelScenarios.LabeledShuffleLoop(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3)]
    [InlineData(0)]
    public void LabelLoop_NativeVaryingRounds_KeepsLocalsDeclaredBeforeTheLabel(int rounds) =>
        UserLabelScenarios.LabelLoop(s_runNative, rounds);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void BranchExitLabel_NativeConditionalFlag_GotoFromLaterStateReachesLabel(int flag) =>
        UserLabelScenarios.BranchExitLabel(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void ChainedLabel_NativeInterleavedSplitLabels_ExecutesInCorrectOrder(int flag) =>
        UserLabelScenarios.ChainedLabel(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void CrossLabelWrite_NativeLocalUsedAfterSplitLabel_PreservesValueAcrossLabels(int flag) =>
        UserLabelScenarios.CrossLabelWrite(s_runNative, flag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void GotoPastInitialization_NativeForwardJump_SkipsDeclarations() =>
        UserLabelScenarios.GotoPastInitialization(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void GotoPastConstAndArrays_NativeForwardJump_SkipsDeclarations() =>
        UserLabelScenarios.GotoPastConstAndArrays(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SiblingSameLabel_NativeConditionalBranches_SpanStaysLocal(int flag) =>
        UserLabelScenarios.SiblingSameLabel(s_runNative, flag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SameStateSameLabel_NativeMultipleGotos_EachGotoReachesItsLabel() =>
        UserLabelScenarios.SameStateSameLabel(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void CrossLabelInitialized_NativeLocalUsedAfterSplitLabel_PreservesInitializedValue(int flag) =>
        UserLabelScenarios.CrossLabelInitialized(s_runNative, flag);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LabeledBarrier_NativeSharedMemory_SeparatesWritesFromReads()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        UserLabelScenarios.LabeledBarrier(s_runNative);
    }
}
