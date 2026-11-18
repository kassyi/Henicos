using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Local functions called from another state than the one that declares them, on the native dialects.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class LocalFunctionAcrossStatesTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeclaredBeforeBarrier_Native_CallsTheFunctionOfAnotherState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchDeclaredBeforeBarrierKernel(data, blockDimX: 4),
            x => x + 5
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CalledBeforeDeclaration_Native_CallsTheFunctionInTwoStates() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchCalledBeforeDeclarationKernel(data, blockDimX: 4),
            _ => 14
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void UnreachableFirstUse_Native_CallsTheFunctionOfAnotherState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchUnreachableFirstUseKernel(data, blockDimX: 4),
            x => x + 5
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CapturesLocalAfterLastBarrier_Native_HoistsTheCapturedLocal() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchCapturesLocalAfterLastBarrierKernel(
                data,
                blockDimX: 4
            ),
            x => x + 3
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SameNameInTwoLoops_Native_KeepsTheFunctionsApart() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchSameNameInTwoLoopsKernel(data, blockDimX: 4),
            x => x + 20
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LabeledLocalFunction_Native_KeepsTheLabelInItsState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchLabeledLocalFunctionKernel(data, blockDimX: 4),
            x => x + 3
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RecursionAcrossBarrier_Native_CallsTheRecursiveFunctionOfAnotherState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchRecursionAcrossBarrierKernel(data, blockDimX: 4),
            x => x + 6
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SyncGridBody_Native_CallsTheFunctionAfterTheGridSync() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchSyncGridBodyKernel(
                data,
                gridDimX: 1,
                blockDimX: 4
            ),
            _ => 11
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NestedAfterReturn_Native_KeepsTheFunctionAfterTheReturn() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchNestedAfterReturnKernel(data, blockDimX: 4),
            x => x + 3
        );

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void TopLevelAfterReturn_Native_KeepsTheFunctionAfterTheReturn() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runNative,
            static data => LocalFunctionAcrossStatesKernels.LaunchTopLevelAfterReturnKernel(data, blockDimX: 4),
            x => x + 3
        );
}
