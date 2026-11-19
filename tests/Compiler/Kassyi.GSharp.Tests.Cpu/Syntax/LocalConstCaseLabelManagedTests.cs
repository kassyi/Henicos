using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Local const values where C++ needs a constant expression or a value, on the managed CPU path.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalConstCaseLabelManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CaseLabel_ManagedLocalConstInt_MatchesTheConstCase() =>
        LocalConstCaseLabelScenarios.CaseLabelInt(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CaseLabel_ManagedLocalConstByte_MatchesTheConstCase() =>
        LocalConstCaseLabelScenarios.CaseLabelByte(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GotoCase_ManagedLocalConst_JumpsToTheConstCase() =>
        LocalConstCaseLabelScenarios.GotoCase(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSection_ManagedConstReadByAnotherSection_KeepsItsValue(int flag) =>
        LocalConstCaseLabelScenarios.SharedSection(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void InArgument_ManagedLocalConst_PassesTheValue() =>
        LocalConstCaseLabelScenarios.InArgument(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LabeledConst_ManagedAtBlockEnd_SkipsNothing() =>
        LocalConstCaseLabelScenarios.LabeledConstAtBlockEnd(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SharedMemoryCount_ManagedLocalConst_DeclaresTheCount() =>
        LocalConstCaseLabelScenarios.SharedMemoryCount(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
