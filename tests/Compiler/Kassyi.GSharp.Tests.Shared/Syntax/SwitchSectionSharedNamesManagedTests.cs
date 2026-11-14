#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback of <see cref="SwitchSectionSharedNamesTests"/>.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SwitchSectionSharedNamesManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OutVarAcrossSections_Managed_DeclaresTheVariableBeforeTheSwitch() =>
        SwitchSectionSharedNamesScenarios.OutVarAcrossSections(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 3)]
    [InlineData(1, 12)]
    public void DeconstructionAcrossSections_Managed_DeclaresTheVariablesBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.DeconstructionAcrossSections(s_runManaged, mode, expected);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    public void LocalFunctionAcrossSections_Managed_DeclaresTheFunctionBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.LocalFunctionAcrossSections(s_runManaged, mode, expected);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 11)]
    [InlineData(1, 102)]
    public void CapturingLocalFunctionAcrossSections_Managed_SharesTheCapturedLocal(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.CapturingLocalFunctionAcrossSections(s_runManaged, mode, expected);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 7)]
    [InlineData(1, 16)]
    public void ChainedLocalFunctionAcrossSections_Managed_SharesTheCalledFunction(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.ChainedLocalFunctionAcrossSections(s_runManaged, mode, expected);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SpanAcrossSections_Managed_DeclaresTheViewBeforeTheSwitch(int mode) =>
        SwitchSectionSharedNamesScenarios.SpanAcrossSections(s_runManaged, mode);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GotoCaseSharedOutVar_Managed_KeepsTheVariableAcrossTheJump() =>
        SwitchSectionSharedNamesScenarios.GotoCaseSharedOutVar(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SharedInLoop_Managed_DeclaresTheVariableOnEveryPass() =>
        SwitchSectionSharedNamesScenarios.SharedInLoop(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0, 6)]
    [InlineData(0, 1, 12)]
    [InlineData(1, 0, 20)]
    public void NestedSwitchShared_Managed_DeclaresEachSwitchsOwnVariable(int outer, int inner, int expected) =>
        SwitchSectionSharedNamesScenarios.NestedSwitchShared(s_runManaged, outer, inner, expected);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 6)]
    [InlineData(1, 8)]
    public void SharedFunctionUsedAboveDeclaration_Managed_WritesItBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.SharedFunction(
            s_runManaged,
            static (output, m) => SwitchSectionSharedNamesKernels.LaunchSharedFunctionUsedAboveDeclarationKernel(
                output,
                m,
                blockDimX: 1
            ),
            mode,
            expected
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 11)]
    [InlineData(1, 20)]
    public void FunctionCallsLaterSectionFunction_Managed_DeclaresTheCalledFunctionFirst(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.SharedFunction(
            s_runManaged,
            static (output, m) => SwitchSectionSharedNamesKernels.LaunchFunctionCallsLaterSectionFunctionKernel(
                output,
                m,
                blockDimX: 1
            ),
            mode,
            expected
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 6)]
    [InlineData(1, 24)]
    public void SharedRecursiveFunction_Managed_WritesTheGroupBeforeTheSwitch(int mode, int expected) =>
        SwitchSectionSharedNamesScenarios.SharedFunction(
            s_runManaged,
            static (output, m) => SwitchSectionSharedNamesKernels.LaunchSharedRecursiveFunctionKernel(
                output,
                m,
                blockDimX: 1
            ),
            mode,
            expected
        );

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
