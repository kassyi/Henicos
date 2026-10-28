#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

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
    public void Test_Managed_LabeledShuffle_RunsAgainOnEveryGoto() => UserLabelScenarios.LabeledShuffleLoop(s_runManaged);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3)]
    [InlineData(0)]
    public void Test_Managed_LabelLoop_KeepsLocalsDeclaredBeforeTheLabel(int rounds) =>
        UserLabelScenarios.LabelLoop(s_runManaged, rounds);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_BranchExitLabel_GotoFromLaterStateReachesLabel(int flag) =>
        UserLabelScenarios.BranchExitLabel(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_ChainedLabel_LabelSplitByAnotherSplitLabel(int flag) =>
        UserLabelScenarios.ChainedLabel(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_CrossLabelWrite_LocalUsedAfterSplitLabel(int flag) =>
        UserLabelScenarios.CrossLabelWrite(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GotoPastInitialization_SkipsTheDeclarations() =>
        UserLabelScenarios.GotoPastInitialization(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GotoPastConstAndArrays_SkipsTheDeclarations() =>
        UserLabelScenarios.GotoPastConstAndArrays(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_SiblingSameLabel_SpanStaysLocal(int flag) =>
        UserLabelScenarios.SiblingSameLabel(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SameStateSameLabel_EachGotoReachesItsLabel() =>
        UserLabelScenarios.SameStateSameLabel(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_CrossLabelInitialized_LocalUsedAfterSplitLabel(int flag) =>
        UserLabelScenarios.CrossLabelInitialized(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_LabeledBarrier_SeparatesWritesFromReads() => UserLabelScenarios.LabeledBarrier(s_runManaged);

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
