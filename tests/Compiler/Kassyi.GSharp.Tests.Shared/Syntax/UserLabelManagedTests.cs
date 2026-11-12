#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback runs the same lowered state machines, so it must match the native dialects.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class UserLabelManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LabeledShuffle_ManagedLoopWithGoto_RunsAgainOnEveryGoto() =>
        UserLabelScenarios.LabeledShuffleLoop(s_runManaged);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3)]
    [InlineData(0)]
    public void LabelLoop_ManagedVaryingRounds_KeepsLocalsDeclaredBeforeTheLabel(int rounds) =>
        UserLabelScenarios.LabelLoop(s_runManaged, rounds);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void BranchExitLabel_ManagedConditionalFlag_GotoFromLaterStateReachesLabel(int flag) =>
        UserLabelScenarios.BranchExitLabel(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void ChainedLabel_ManagedInterleavedSplitLabels_ExecutesInCorrectOrder(int flag) =>
        UserLabelScenarios.ChainedLabel(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void CrossLabelWrite_ManagedLocalUsedAfterSplitLabel_PreservesValueAcrossLabels(int flag) =>
        UserLabelScenarios.CrossLabelWrite(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GotoPastInitialization_ManagedForwardJump_SkipsDeclarations() =>
        UserLabelScenarios.GotoPastInitialization(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GotoPastConstAndArrays_ManagedForwardJump_SkipsDeclarations() =>
        UserLabelScenarios.GotoPastConstAndArrays(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SiblingSameLabel_ManagedConditionalBranches_SpanStaysLocal(int flag) =>
        UserLabelScenarios.SiblingSameLabel(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SameStateSameLabel_ManagedMultipleGotos_EachGotoReachesItsLabel() =>
        UserLabelScenarios.SameStateSameLabel(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void CrossLabelInitialized_ManagedLocalUsedAfterSplitLabel_PreservesInitializedValue(int flag) =>
        UserLabelScenarios.CrossLabelInitialized(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LabeledBarrier_ManagedSharedMemory_SeparatesWritesFromReads() =>
        UserLabelScenarios.LabeledBarrier(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
