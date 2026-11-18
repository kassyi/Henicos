using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Local functions called from another state than the one that declares them, on the managed C# fallback.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalFunctionAcrossStatesManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DeclaredBeforeBarrier_Managed_CallsTheFunctionOfAnotherState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchDeclaredBeforeBarrierKernel(data, blockDimX: 4),
            x => x + 5
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CalledBeforeDeclaration_Managed_CallsTheFunctionInTwoStates() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchCalledBeforeDeclarationKernel(data, blockDimX: 4),
            _ => 14
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UnreachableFirstUse_Managed_CallsTheFunctionOfAnotherState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchUnreachableFirstUseKernel(data, blockDimX: 4),
            x => x + 5
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CapturesLocalAfterLastBarrier_Managed_HoistsTheCapturedLocal() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchCapturesLocalAfterLastBarrierKernel(
                data,
                blockDimX: 4
            ),
            x => x + 3
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SameNameInTwoLoops_Managed_KeepsTheFunctionsApart() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchSameNameInTwoLoopsKernel(data, blockDimX: 4),
            x => x + 20
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LabeledLocalFunction_Managed_KeepsTheLabelInItsState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchLabeledLocalFunctionKernel(data, blockDimX: 4),
            x => x + 3
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RecursionAcrossBarrier_Managed_CallsTheRecursiveFunctionOfAnotherState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchRecursionAcrossBarrierKernel(data, blockDimX: 4),
            x => x + 6
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SyncGridBody_Managed_CallsTheFunctionAfterTheGridSync() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchSyncGridBodyKernel(
                data,
                gridDimX: 1,
                blockDimX: 4
            ),
            _ => 11
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NestedAfterReturn_Managed_KeepsTheFunctionAfterTheReturn() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchNestedAfterReturnKernel(data, blockDimX: 4),
            x => x + 3
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TopLevelAfterReturn_Managed_KeepsTheFunctionAfterTheReturn() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchTopLevelAfterReturnKernel(data, blockDimX: 4),
            x => x + 3
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UsingCaptured_Managed_KeepsTheDisposalOfTheCapturedVariable() =>
        LocalFunctionAcrossStatesScenarios.RunWithLog(
            s_runManaged,
            static (log, o) => LocalFunctionAcrossStatesKernels.LaunchUsingCapturedKernel(log, o, blockDimX: 1),
            [1, 7, 0, 0],
            7
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OutVariableCaptured_Managed_ReadsTheVariableOfItsState() =>
        LocalFunctionAcrossStatesScenarios.Run(
            s_runManaged,
            static data => LocalFunctionAcrossStatesKernels.LaunchOutVariableCapturedKernel(data, blockDimX: 4),
            x => x % 2 + 10
        );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ChainToStayingFunction_Managed_KeepsTheCallerWithTheCallee() =>
        LocalFunctionAcrossStatesScenarios.RunWithLog(
            s_runManaged,
            static (log, o) => LocalFunctionAcrossStatesKernels.LaunchChainToStayingFunctionKernel(
                log,
                o,
                blockDimX: 1
            ),
            [1, 5, 0, 0],
            6
        );

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
