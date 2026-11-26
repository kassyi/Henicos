#nullable enable
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for non-suspending struct instance <c>[GpuDeviceFunction]</c> methods across managed, native,
/// and CUDA paths, verifying receiver semantics against the C# oracle.
/// </summary>
public class InstanceDeviceFunctionDifferentialTests
{
    private const int ThreadCount = 4;

    private static int[] SequentialData()
    {
        var data = new int[ThreadCount];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i + 1;
        }

        return data;
    }

    private static DifferentialCase CreateSingleOutputCase(
        DiffLaunch<int[], int[]> launch,
        int valuesPerThread,
        Action<int[]> assertThreadZero,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        new(DiffKernelNames.FromLaunchExpression(launchExpression, paramName: nameof(launch)))
        {
            Shape = LaunchShape.ForRows(ThreadCount, blockX: ThreadCount),
            Seeds = [0],
            Inputs = (_, _) => new DiffBuffers()
                .In("data", SequentialData(), OutputPolicy.Exact)
                .Out<int>("r", ThreadCount * valuesPerThread, OutputPolicy.Exact),
            OracleIsLaunch = true,
            Launch = (b, s) => launch(
                b.Get<int>("data"),
                b.Get<int>("r"),
                s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, 0, default, default),
            Verify = (actual, _) =>
            {
                assertThreadZero(actual.Get<int>("r"));
                return PropertyResult.Passed;
            },
        };

    private static DifferentialCase CreateItemsCase(
        DiffLaunch<int[], DiffInstanceCounter[], int[]> launch,
        Action<DiffInstanceCounter[], int[]> assertThreadZero,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        new(DiffKernelNames.FromLaunchExpression(launchExpression, paramName: nameof(launch)))
        {
            Shape = LaunchShape.ForRows(ThreadCount, blockX: ThreadCount),
            Seeds = [0],
            Inputs = (_, _) => new DiffBuffers()
                .In("data", SequentialData(), OutputPolicy.Exact)
                .In("items", new DiffInstanceCounter[ThreadCount * 3])
                .Out<int>("r", ThreadCount * 2, OutputPolicy.Exact),
            OracleIsLaunch = true,
            Launch = (b, s) => launch(
                b.Get<int>("data"),
                b.Get<DiffInstanceCounter>("items"),
                b.Get<int>("r"),
                s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, 0, default, default),
            Verify = (actual, _) =>
            {
                assertThreadZero(actual.Get<DiffInstanceCounter>("items"), actual.Get<int>("r"));
                return PropertyResult.Passed;
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void RefReceiverArgWrites_AliasedArgWrite_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchRefReceiverArgWritesKernel,
            valuesPerThread: 1,
            static r => Assert.Equal(102, r[0])));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void ValueReceiverMutating_RvalueReceiver_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchValueReceiverMutatingKernel,
            valuesPerThread: 1,
            static r => Assert.Equal(10, r[0])));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void ReadonlyMemberAliasWrite_ReceiverSeesAliasWrite_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchReadonlyMemberAliasWriteKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(500, r[0]);
                Assert.Equal(500, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void MutatingMemberAliasWrite_ReceiverSeesAliasWrite_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchMutatingMemberAliasWriteKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(600, r[0]);
                Assert.Equal(600, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void InReceiverCopyBeforeArgs_CopiedBeforeAliasedArgWrite_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchInReceiverCopyBeforeArgsKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(5, r[0]);
                Assert.Equal(104, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void InParamMutatingCall_MutatesCopyOnly_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchInParamMutatingCallKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(10005, r[0]);
                Assert.Equal(5, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void ThisReassign_ResetsReceiverThroughPointer_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchThisReassignKernel,
            valuesPerThread: 1,
            static r => Assert.Equal(-1, r[0])));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void ImplicitAndExplicitThis_MutatesSameReceiver_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchImplicitAndExplicitThisKernel,
            valuesPerThread: 1,
            static r => Assert.Equal(9, r[0])));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ReadonlyCallsMutating_CopiesThisOnMutatingCall_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchReadonlyCallsMutatingKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(18008, r[0]);
                Assert.Equal(8, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ReadonlyFieldReceiver_CopiesFieldOnEachMutatingCall_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchReadonlyFieldReceiverKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(10109, r[0]);
                Assert.Equal(9, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldOfLocalReceiver_MutatesLocalField_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchFieldOfLocalReceiverKernel,
            valuesPerThread: 1,
            static r => Assert.Equal(14, r[0])));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void AutoPropertyReceiver_MutatesTemporaryNotBackingField_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchAutoPropertyReceiverKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(14, r[0]);
                Assert.Equal(9, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ReceiverBeforeArgs_RefReturningReceiverEvaluatedFirst_MatchesOracle() =>
        DifferentialRunner.Run(CreateItemsCase(
            InstanceDeviceFunctionKernels.LaunchReceiverBeforeArgsKernel,
            static (items, r) =>
            {
                Assert.Equal(12, r[0]);
                Assert.Equal(1, r[1]);
                Assert.Equal(3, items[0].X);
                Assert.Equal(0, items[1].X);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ElementReceiverIndexSideEffect_IndexSideEffectEvaluatedFirst_MatchesOracle() =>
        DifferentialRunner.Run(CreateItemsCase(
            InstanceDeviceFunctionKernels.LaunchElementReceiverIndexSideEffectKernel,
            static (items, r) =>
            {
                Assert.Equal(10, items[0].X);
                Assert.Equal(0, items[1].X);
                Assert.Equal(10, r[0]);
                Assert.Equal(0, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void StructMembersCallInstanceDeviceFunction_CtorAndGetters_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchStructMembersCallInstanceDeviceFunctionKernel,
            valuesPerThread: 6,
            static r =>
            {
                Assert.Equal(6, r[0]);
                Assert.Equal(7, r[1]);
                Assert.Equal(7, r[2]);
                Assert.Equal(17, r[3]);
                Assert.Equal(7, r[4]);
                Assert.Equal(107, r[5]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void ReadOnlyStructDeviceFunction_CalledOnInParam_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchReadOnlyStructDeviceFunctionKernel,
            valuesPerThread: 1,
            static r => Assert.Equal(1408, r[0])));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void StaticAndInstanceOverload_DistinctNamesAndValues_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchStaticAndInstanceOverloadKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(31, r[0]);
                Assert.Equal(35, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void RvalueLvalueAliasReadonly_AssignAndConditionalReceiversCopyBeforeBind_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchRvalueLvalueAliasReadonlyKernel,
            valuesPerThread: 6,
            static r =>
            {
                Assert.Equal(2, r[0]);
                Assert.Equal(500, r[1]);
                Assert.Equal(10, r[2]);
                Assert.Equal(500, r[3]);
                Assert.Equal(4, r[4]);
                Assert.Equal(500, r[5]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void RvalueLvalueMemberChainMutating_ConditionalAndAssignmentChains_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchRvalueLvalueMemberChainMutatingKernel,
            valuesPerThread: 4,
            static r =>
            {
                Assert.Equal(8, r[0]);
                Assert.Equal(1, r[1]);
                Assert.Equal(38, r[2]);
                Assert.Equal(31, r[3]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void PrimaryConstructorDeviceFunction_ReadsAndMutatesCapturedParam_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchPrimaryConstructorDeviceFunctionKernel,
            valuesPerThread: 3,
            static r =>
            {
                Assert.Equal(11, r[0]);
                Assert.Equal(14, r[1]);
                Assert.Equal(14, r[2]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConstInferredGetterField_CallsNonReadonlyPureOnFieldChain_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchConstInferredGetterFieldKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(106, r[0]);
                Assert.Equal(116, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ParenthesizedMemberCallOrder_LeftToRightEvaluation_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchParenthesizedMemberCallOrderKernel,
            valuesPerThread: 3,
            static r =>
            {
                Assert.Equal(21, r[0]);
                Assert.Equal(22, r[1]);
                Assert.Equal(21, r[2]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void RefReadOnlyConditionalReceiver_CopiesOnMutatingAndAliasesOnReadOnly_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchRefReadOnlyConditionalReceiverKernel,
            valuesPerThread: 6,
            static r =>
            {
                Assert.Equal(11, r[0]);
                Assert.Equal(11, r[1]);
                Assert.Equal(10, r[2]);
                Assert.Equal(20, r[3]);
                Assert.Equal(500, r[4]);
                Assert.Equal(500, r[5]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConstConditionRefConditional_FollowsSelectedBranchMutability_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchConstConditionRefConditionalKernel,
            valuesPerThread: 4,
            static r =>
            {
                Assert.Equal(211021, r[0]);
                Assert.Equal(212110, r[1]);
                Assert.Equal(211021, r[2]);
                Assert.Equal(111020, r[3]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ParenthesizedThisField_MutatesInCtorAndMethod_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchParenthesizedThisFieldKernel,
            valuesPerThread: 3,
            static r =>
            {
                Assert.Equal(15, r[0]);
                Assert.Equal(15, r[1]);
                Assert.Equal(15, r[2]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void InlineArrayInParam_MutatingElementCopiesWithoutMutatingInput_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchInlineArrayInParamKernel,
            valuesPerThread: 2,
            static r =>
            {
                Assert.Equal(1110, r[0]);
                Assert.Equal(1111, r[1]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void RvalueSubobjectTemporaryReceiver_InlineArrayAndFieldChains_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchRvalueSubobjectTemporaryReceiverKernel,
            valuesPerThread: 12,
            static r =>
            {
                Assert.Equal(12, r[0]);
                Assert.Equal(12, r[1]);
                Assert.Equal(22, r[2]);
                Assert.Equal(21, r[3]);
                Assert.Equal(32, r[4]);
                Assert.Equal(31, r[5]);
                Assert.Equal(42, r[6]);
                Assert.Equal(52, r[7]);
                Assert.Equal(62, r[8]);
                Assert.Equal(72, r[9]);
                Assert.Equal(17106, r[10]);
                Assert.Equal(27106, r[11]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ReadOnlyInlineArrayTemporaryReceiver_CopiesInsteadOfAliasing_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchReadOnlyInlineArrayTemporaryReceiverKernel,
            valuesPerThread: 10,
            static r =>
            {
                Assert.Equal(21, r[0]);
                Assert.Equal(500, r[1]);
                Assert.Equal(26, r[2]);
                Assert.Equal(31, r[3]);
                Assert.Equal(999, r[4]);
                Assert.Equal(36, r[5]);
                Assert.Equal(999, r[6]);
                Assert.Equal(56, r[7]);
                Assert.Equal(76, r[8]);
                Assert.Equal(86086, r[9]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConstConditionInlineArrayReceiver_MixedRefAndInCopiesDefensively_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchConstConditionInlineArrayReceiverKernel,
            valuesPerThread: 4,
            static r =>
            {
                Assert.Equal(4241, r[0]);
                Assert.Equal(4241, r[1]);
                Assert.Equal(5252, r[2]);
                Assert.Equal(6261, r[3]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConstInferredGetterSubobject_InlineArrayAndRefCondReceivers_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchConstInferredGetterSubobjectKernel,
            valuesPerThread: 10,
            static r =>
            {
                Assert.Equal(111, r[0]);
                Assert.Equal(111, r[1]);
                Assert.Equal(111, r[2]);
                Assert.Equal(111, r[3]);
                Assert.Equal(111, r[4]);
                Assert.Equal(111, r[5]);
                Assert.Equal(150, r[6]);
                Assert.Equal(111, r[7]);
                Assert.Equal(100, r[8]);
                Assert.Equal(111, r[9]);
            }));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void PointerArrowReceiver_PointerMemberCalls_MatchesOracle() =>
        DifferentialRunner.Run(CreateSingleOutputCase(
            InstanceDeviceFunctionKernels.LaunchPointerArrowReceiverKernel,
            valuesPerThread: 11,
            static r =>
            {
                Assert.Equal(6, r[0]);
                Assert.Equal(107, r[1]);
                Assert.Equal(16, r[2]);
                Assert.Equal(2626, r[3]);
                Assert.Equal(4646, r[4]);
                Assert.Equal(152152, r[5]);
                Assert.Equal(161, r[6]);
                Assert.Equal(727281, r[7]);
                Assert.Equal(96096, r[8]);
                Assert.Equal(500500, r[9]);
                Assert.Equal(500500, r[10]);
            }));
}
