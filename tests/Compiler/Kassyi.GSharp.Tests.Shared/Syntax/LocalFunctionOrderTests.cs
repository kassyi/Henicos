using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Local functions used before their declaration, on the native dialects.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class LocalFunctionOrderTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CallBeforeDeclaration_Native_UsesTheLocalFunctionAboveItsDeclaration() =>
        LocalFunctionOrderScenarios.Run(
            s_runNative,
            static data => LocalFunctionOrderKernels.LaunchCallBeforeDeclarationKernel(data, blockDimX: 4),
            x => x + 1
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CapturingCallBeforeDeclaration_Native_CapturesTheLocalsAboveTheCall() =>
        LocalFunctionOrderScenarios.Run(
            s_runNative,
            static data => LocalFunctionOrderKernels.LaunchCapturingCallBeforeDeclarationKernel(data, 3, blockDimX: 4),
            x => x * 6 + 3
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CallChainBeforeDeclaration_Native_WritesTheUsedFunctionFirst() =>
        LocalFunctionOrderScenarios.Run(
            s_runNative,
            static data => LocalFunctionOrderKernels.LaunchCallChainBeforeDeclarationKernel(data, blockDimX: 4),
            x => (x + 10) * 2
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void NestedCallBeforeDeclaration_Native_UsesTheLocalFunctionInTheNestedBlock(int flag) =>
        LocalFunctionOrderScenarios.NestedCallBeforeDeclaration(s_runNative, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(7)]
    public void SelfRecursion_Native_CallsItself(int bonus) =>
        LocalFunctionOrderScenarios.SelfRecursion(s_runNative, bonus);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void MutualRecursion_Native_CallsTheOtherFunction() =>
        LocalFunctionOrderScenarios.MutualRecursion(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void VoidRecursion_Native_WritesThroughTheCapturedArray() =>
        LocalFunctionOrderScenarios.VoidRecursion(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RecursionBeforeDeclaration_Native_UsesTheGroupAboveItsDeclaration() =>
        LocalFunctionOrderScenarios.RecursionBeforeDeclaration(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionCallBeforeDeclaration_Native_UsesTheLocalFunctionOfTheSection(int mode) =>
        LocalFunctionOrderScenarios.SwitchSectionCallBeforeDeclaration(s_runNative, mode);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UsingBodyCallBeforeDeclaration_Native_UsesTheLocalFunctionInTheUsingBody() =>
        LocalFunctionOrderScenarios.UsingBodyCallBeforeDeclaration(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DelegateOfRecursive_Native_ConvertsTheLocalFunctionGroup() =>
        LocalFunctionOrderScenarios.DelegateOfRecursive(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UnreachableCallBeforeCapturedDeclaration_Native_DeclaresTheCapturedLocalsFirst() =>
        LocalFunctionOrderScenarios.Run(
            s_runNative,
            static data => LocalFunctionOrderKernels.LaunchUnreachableCallBeforeCapturedDeclarationKernel(data, blockDimX: 4),
            x => x + 5
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UnreachableCallTwoCapturedLocals_Native_DeclaresTheCapturedLocalsFirst() =>
        LocalFunctionOrderScenarios.Run(
            s_runNative,
            static data => LocalFunctionOrderKernels.LaunchUnreachableCallTwoCapturedLocalsKernel(data, blockDimX: 4),
            x => x + 7
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UnreachableCallThroughAFunction_Native_DeclaresTheCapturedLocalsFirst() =>
        LocalFunctionOrderScenarios.Run(
            s_runNative,
            static data => LocalFunctionOrderKernels.LaunchUnreachableCallThroughAFunctionKernel(data, blockDimX: 4),
            x => x + 6
        );
}
