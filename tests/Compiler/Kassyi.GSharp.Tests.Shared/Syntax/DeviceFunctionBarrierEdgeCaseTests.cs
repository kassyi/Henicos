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
    public void Test_SharedMemoryOfSuspendingCallee_DoesNotAliasCallerBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndSuspendingCalleeSharedMemory(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SharedMemoryOfNonSuspendingCallee_DoesNotAliasCallerBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.CallerAndPlainCalleeSharedMemory(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SharedMemoryOfCallee_IsSharedAcrossCallSites()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.SharedMemoryPerDeclaration(s_runNative);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void Test_SuspendingCallOnRightOfAnd_RunsOnlyWhenLeftIsTrue(int enabled) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ShortCircuitAnd(s_runNative, enabled);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_SuspendingCallOnRightOfOr_RunsOnlyWhenLeftIsFalse(int skip) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ShortCircuitOr(s_runNative, skip);

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(0)]
    public void Test_SuspendingCallInConditionalOperator_RunsOnlyOnSelectedBranch(int useShuffle) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ConditionalOperator(s_runNative, useShuffle);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void Test_SuspendingCallInWhileCondition_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.WhileCondition(s_runNative, limit);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void Test_SuspendingCallInForIncrementor_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.ForIncrementor(s_runNative, limit);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void Test_SuspendingCallInDoWhileCondition_IsEvaluatedEveryIteration(int limit) =>
        DeviceFunctionBarrierEdgeCaseScenarios.DoWhileCondition(s_runNative, limit);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExpressionBodiedSuspendingDeviceFunction_IsLowered() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodyShiftUp(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExpressionBodiedKernelCallingSuspendingFunction_IsLowered() =>
        DeviceFunctionBarrierEdgeCaseScenarios.ExpressionBodiedKernel(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefParameterOfSuspendingCallee_KeepsWritesAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.RefParameter(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefParameterPassedToNestedSuspendingCallee_KeepsWritesAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.RefParameterChain(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefElementArgumentWithSideEffect_IsEvaluatedOnceAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.RefElementArgument(s_runNative);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Test_OutArgumentDeclaredInLoop_IsWrittenEveryIteration(int rounds)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceFunctionBarrierEdgeCaseScenarios.OutParameterInLoop(s_runNative, rounds);
    }
}
