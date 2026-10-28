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
    public void Test_LabeledShuffle_RunsAgainOnEveryGoto() => UserLabelScenarios.LabeledShuffleLoop(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3)]
    [InlineData(0)]
    public void Test_LabelLoop_KeepsLocalsDeclaredBeforeTheLabel(int rounds) =>
        UserLabelScenarios.LabelLoop(s_runNative, rounds);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_BranchExitLabel_GotoFromLaterStateReachesLabel(int flag) =>
        UserLabelScenarios.BranchExitLabel(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_ChainedLabel_LabelSplitByAnotherSplitLabel(int flag) =>
        UserLabelScenarios.ChainedLabel(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_CrossLabelWrite_LocalUsedAfterSplitLabel(int flag) =>
        UserLabelScenarios.CrossLabelWrite(s_runNative, flag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GotoPastInitialization_SkipsTheDeclarations() =>
        UserLabelScenarios.GotoPastInitialization(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GotoPastConstAndArrays_SkipsTheDeclarations() =>
        UserLabelScenarios.GotoPastConstAndArrays(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_CrossLabelInitialized_LocalUsedAfterSplitLabel(int flag) =>
        UserLabelScenarios.CrossLabelInitialized(s_runNative, flag);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LabeledBarrier_SeparatesWritesFromReads()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        UserLabelScenarios.LabeledBarrier(s_runNative);
    }
}
