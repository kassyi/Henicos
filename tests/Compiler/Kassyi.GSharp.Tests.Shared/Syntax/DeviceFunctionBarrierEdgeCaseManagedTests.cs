#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback runs the same lowered state machines, so it must match the native dialects.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class DeviceFunctionBarrierEdgeCaseManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryOfSuspendingCallee_ManagedExecution_DoesNotAliasCallerBuffer() =>
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndSuspendingCalleeSharedMemory(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryOfNonSuspendingCallee_ManagedExecution_DoesNotAliasCallerBuffer() =>
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndPlainCalleeSharedMemory(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SharedMemoryOfCallee_ManagedExecutionMultipleCallSites_IsSharedAcrossCallSites() =>
        DeviceFunctionBarrierEdgeCaseScenarios.SharedMemoryPerDeclaration(s_runManaged);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void SuspendingCallOnRightOfAnd_ManagedExecutionConditionalBranch_RunsOnlyWhenLeftIsTrue(int enabled) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ShortCircuitAnd(s_runManaged, enabled);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void SuspendingCallInConditionalOperator_ManagedExecutionSelectedBranch_RunsOnlyOnSelectedBranch(
        int useShuffle) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ConditionalOperator(s_runManaged, useShuffle);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void SuspendingCallInWhileCondition_ManagedExecutionLoop_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.WhileCondition(s_runManaged, limit);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void SuspendingCallInForIncrementor_ManagedExecutionLoop_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ForIncrementor(s_runManaged, limit);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpressionBodiedSuspendingDeviceFunction_ManagedExecution_LowersAndExecutes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodyShiftUp(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpressionBodiedKernelCallingSuspendingFunction_ManagedExecution_LowersAndExecutes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodiedKernel(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RefParameterPassedToNestedSuspendingCallee_ManagedExecutionNestedResumes_KeepsWritesAcrossResumes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.RefParameterChain(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RefElementArgumentWithSideEffect_ManagedExecutionResumes_IsEvaluatedOnceAcrossResumes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.RefElementArgument(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(3)]
    public void OutArgumentDeclaredInLoop_ManagedExecutionIterations_IsWrittenEveryIteration(int rounds) =>
        DeviceFunctionBarrierEdgeCaseScenarios.OutParameterInLoop(s_runManaged, rounds);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
