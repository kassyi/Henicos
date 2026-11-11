using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Edge cases of device functions that suspend, on the native dialects. CUDA is the reference: the CPU dialect must
/// produce the same results.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class DeviceFunctionBarrierEdgeCaseTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryOfSuspendingCallee_CallerAndCalleeBuffers_DoesNotAliasCallerBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndSuspendingCalleeSharedMemory(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryOfNonSuspendingCallee_CallerAndCalleeBuffers_DoesNotAliasCallerBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndPlainCalleeSharedMemory(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SharedMemoryOfCallee_MultipleCallSites_IsSharedAcrossCallSites()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.SharedMemoryPerDeclaration(s_runNative);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void SuspendingCallOnRightOfAnd_ConditionalBranch_RunsOnlyWhenLeftIsTrue(int enabled) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ShortCircuitAnd(s_runNative, enabled);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void SuspendingCallOnRightOfOr_ConditionalBranch_RunsOnlyWhenLeftIsFalse(int skip) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ShortCircuitOr(s_runNative, skip);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void SuspendingCallInConditionalOperator_SelectedBranch_RunsOnlyOnSelectedBranch(int useShuffle) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ConditionalOperator(s_runNative, useShuffle);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void SuspendingCallInWhileCondition_LoopExecution_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.WhileCondition(s_runNative, limit);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void SuspendingCallInForIncrementor_LoopExecution_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ForIncrementor(s_runNative, limit);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void SuspendingCallInDoWhileCondition_LoopExecution_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.DoWhileCondition(s_runNative, limit);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpressionBodiedSuspendingDeviceFunction_KernelExecution_LowersAndExecutes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodyShiftUp(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpressionBodiedKernelCallingSuspendingFunction_KernelExecution_LowersAndExecutes() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodiedKernel(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RefParameterOfSuspendingCallee_MultipleResumes_KeepsWritesAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.RefParameter(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RefParameterPassedToNestedSuspendingCallee_NestedResumes_KeepsWritesAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.RefParameterChain(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RefElementArgumentWithSideEffect_MultipleResumes_IsEvaluatedOnceAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.RefElementArgument(s_runNative);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void OutArgumentDeclaredInLoop_MultipleIterations_IsWrittenEveryIteration(int rounds)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.OutParameterInLoop(s_runNative, rounds);
    }
}
