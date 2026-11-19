using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Local const values where C++ needs a constant expression or a value, on the native CPU path (Tests.Cpu) and on CUDA
/// (Tests.Cuda).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class LocalConstCaseLabelTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runDefault = static launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CaseLabel_LocalConstInt_MatchesTheConstCase() =>
        LocalConstCaseLabelScenarios.CaseLabelInt(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CaseLabel_LocalConstByte_MatchesTheConstCase() =>
        LocalConstCaseLabelScenarios.CaseLabelByte(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void GotoCase_LocalConst_JumpsToTheConstCase() =>
        LocalConstCaseLabelScenarios.GotoCase(s_runDefault);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSection_ConstReadByAnotherSection_KeepsItsValue(int flag) =>
        LocalConstCaseLabelScenarios.SharedSection(s_runDefault, flag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InArgument_LocalConst_PassesTheValue() =>
        LocalConstCaseLabelScenarios.InArgument(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LabeledConst_AtBlockEnd_JumpSkipsOnlyTheAssignment() =>
        LocalConstCaseLabelScenarios.LabeledConstAtBlockEnd(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SharedMemoryCount_LocalConst_DeclaresTheCount() =>
        LocalConstCaseLabelScenarios.SharedMemoryCount(s_runDefault);
}
