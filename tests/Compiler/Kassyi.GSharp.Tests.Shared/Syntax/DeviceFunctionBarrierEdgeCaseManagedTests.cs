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
public class DeviceFunctionBarrierEdgeCaseManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SharedMemoryOfSuspendingCallee_DoesNotAliasCallerBuffer() =>
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndSuspendingCalleeSharedMemory(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SharedMemoryOfNonSuspendingCallee_DoesNotAliasCallerBuffer() =>
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndPlainCalleeSharedMemory(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SharedMemoryOfCallee_IsSharedAcrossCallSites() =>
        DeviceFunctionBarrierEdgeCaseScenarios.SharedMemoryPerDeclaration(s_runManaged);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void Test_Managed_SuspendingCallOnRightOfAnd_RunsOnlyWhenLeftIsTrue(int enabled) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ShortCircuitAnd(s_runManaged, enabled);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void Test_Managed_SuspendingCallInConditionalOperator_RunsOnlyOnSelectedBranch(int useShuffle) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ConditionalOperator(s_runManaged, useShuffle);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void Test_Managed_SuspendingCallInWhileCondition_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.WhileCondition(s_runManaged, limit);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void Test_Managed_SuspendingCallInForIncrementor_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ForIncrementor(s_runManaged, limit);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ExpressionBodiedSuspendingDeviceFunction_IsLowered() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodyShiftUp(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ExpressionBodiedKernelCallingSuspendingFunction_IsLowered() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodiedKernel(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_RefParameterPassedToNestedSuspendingCallee_KeepsWritesAcrossResumes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.RefParameterChain(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RefElementArgumentWithSideEffect_IsEvaluatedOnceAcrossResumes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.RefElementArgument(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(3)]
    public void Test_Managed_OutArgumentDeclaredInLoop_IsWrittenEveryIteration(int rounds) =>
        DeviceFunctionBarrierEdgeCaseScenarios.OutParameterInLoop(s_runManaged, rounds);

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
