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

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SpanAcrossSections_Native_DeclaresTheViewBeforeTheSwitch(int mode) =>
        SwitchSectionSharedNamesScenarios.SpanAcrossSections(s_runNative, mode);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void GotoCaseSharedOutVar_Native_KeepsTheVariableAcrossTheJump() =>
        SwitchSectionSharedNamesScenarios.GotoCaseSharedOutVar(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SharedInLoop_Native_DeclaresTheVariableOnEveryPass() =>
        SwitchSectionSharedNamesScenarios.SharedInLoop(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0, 6)]
    [InlineData(0, 1, 12)]
    [InlineData(1, 0, 20)]
    public void NestedSwitchShared_Native_DeclaresEachSwitchsOwnVariable(int outer, int inner, int expected) =>
        SwitchSectionSharedNamesScenarios.NestedSwitchShared(s_runNative, outer, inner, expected);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 6)]
    [InlineData(1, 8)]
    public void SharedFunctionUsedAboveDeclaration_Native_WritesItBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.SharedFunction(
            s_runNative,
            static (output, m) => SwitchSectionSharedNamesKernels.LaunchSharedFunctionUsedAboveDeclarationKernel(
                output,
                m,
                blockDimX: 1
            ),
            mode,
            expected
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 11)]
    [InlineData(1, 20)]
    public void FunctionCallsLaterSectionFunction_Native_DeclaresTheCalledFunctionFirst(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.SharedFunction(
            s_runNative,
            static (output, m) => SwitchSectionSharedNamesKernels.LaunchFunctionCallsLaterSectionFunctionKernel(
                output,
                m,
                blockDimX: 1
            ),
            mode,
            expected
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 6)]
    [InlineData(1, 24)]
    public void SharedRecursiveFunction_Native_WritesTheGroupBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.SharedFunction(
            s_runNative,
            static (output, m) => SwitchSectionSharedNamesKernels.LaunchSharedRecursiveFunctionKernel(
                output,
                m,
                blockDimX: 1
            ),
            mode,
            expected
        );
}
