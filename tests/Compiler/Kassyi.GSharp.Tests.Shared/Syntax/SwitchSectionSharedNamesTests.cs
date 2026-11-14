using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Expression variables and local functions shared by switch sections, on the native dialects.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SwitchSectionSharedNamesTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void OutVarAcrossSections_Native_DeclaresTheVariableBeforeTheSwitch() =>
        SwitchSectionSharedNamesScenarios.OutVarAcrossSections(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 3)]
    [InlineData(1, 12)]
    public void DeconstructionAcrossSections_Native_DeclaresTheVariablesBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.DeconstructionAcrossSections(s_runNative, mode, expected);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    public void LocalFunctionAcrossSections_Native_DeclaresTheFunctionBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.LocalFunctionAcrossSections(s_runNative, mode, expected);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 11)]
    [InlineData(1, 102)]
    public void CapturingLocalFunctionAcrossSections_Native_SharesTheCapturedLocal(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.CapturingLocalFunctionAcrossSections(s_runNative, mode, expected);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 7)]
    [InlineData(1, 16)]
    public void ChainedLocalFunctionAcrossSections_Native_SharesTheCalledFunction(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.ChainedLocalFunctionAcrossSections(s_runNative, mode, expected);
}
