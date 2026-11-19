#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of the null-conditional access <c>?.</c>, one case per shape. A kernel without a barrier runs
/// its own C# as the oracle; a kernel with a barrier is compared with a C# model of it (the receiver is null on the
/// rows whose input is even, so one block mixes null and non-null receivers).
/// </summary>
public class ConditionalAccessDifferentialTests
{
    private const int Rows = 64;

    private static DifferentialCase Case(string kernel, string[] columns, Action<DiffBuffers, LaunchShape> launch) =>
        new($"{nameof(ConditionalAccessDifferentialKernels)}.{kernel}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            Launch = launch,
            OracleIsLaunch = true,
        };

    // A kernel with a barrier: expected gives the columns of a row from the input and the row
    private static DifferentialCase BarrierCase(string kernel, string[] columns,
        Action<DiffBuffers, LaunchShape> launch, Func<int[], int, int[]> expected) =>
        new($"{nameof(ConditionalAccessDifferentialKernels)}.{kernel}")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            Launch = launch,
            Reference = (b, _) =>
            {
                var a = b.Get<int>("a");
                var dst = b.Get<int>("dst");
                for (int t = 0; t < a.Length; t++)
                {
                    var row = expected(a, t);
                    for (int c = 0; c < columns.Length; c++)
                    {
                        dst[t * columns.Length + c] = row[c];
                    }
                }
            },
        };

    private static bool IsNullRow(int[] a, int t) => (a[t] & 1) == 0;

    internal static readonly DifferentialCase s_array = Case(
        nameof(ConditionalAccessDifferentialKernels.ArrayConditionalAccessKernel),
        ["length_has_value", "length", "element_has_value", "element", "length_or_default"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchArrayConditionalAccessKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_nullableStruct = Case(
        nameof(ConditionalAccessDifferentialKernels.NullableStructConditionalAccessKernel),
        ["x_has_value", "x", "y_has_value", "y", "sum_with_defaults"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchNullableStructConditionalAccessKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_chain = Case(
        nameof(ConditionalAccessDifferentialKernels.ChainedConditionalAccessKernel),
        ["length_has_value", "length", "first_has_value", "first", "length_or_default"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchChainedConditionalAccessKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_receiverOnce = Case(
        nameof(ConditionalAccessDifferentialKernels.ReceiverEvaluatedOnceKernel),
        ["receiver_evaluations", "x", "y"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchReceiverEvaluatedOnceKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_assignment = Case(
        nameof(ConditionalAccessDifferentialKernels.ConditionalAssignmentKernel),
        ["assigned_or_sentinel", "right_side_evaluations", "assignment_value", "assigned_value_slot"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchConditionalAssignmentKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_stackArray = Case(
        nameof(ConditionalAccessDifferentialKernels.StackArrayConditionalAccessKernel),
        ["length", "element", "element_plus_one"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchStackArrayConditionalAccessKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_suspendingReceiver = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.SuspendingReceiverKernel),
        ["receiver_evaluations", "has_value", "x"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel(
            b.Get<int>("a"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [1, 0, -1] : [1, 1, 7]);

    internal static readonly DifferentialCase s_accessBeforeBarrier = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.AccessBeforeBarrierKernel),
        ["index_evaluations", "has_value", "value"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchAccessBeforeBarrierKernel(
            b.Get<int>("a"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [0, 0, -1] : [1, 1, a[1]]);

    internal static readonly DifferentialCase s_accessInBarrierLoop = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.AccessInBarrierLoopKernel),
        ["index_evaluations", "sum"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchAccessInBarrierLoopKernel(
            b.Get<int>("a"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [0, -2] : [2, unchecked(a[0] + a[1])]);

    internal static readonly DifferentialCase s_suspendingCallBesideAccess = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.SuspendingCallBesideAccessKernel),
        ["index_evaluations", "call_evaluations", "access_then_call", "call_then_access"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchSuspendingCallBesideAccessKernel(
            b.Get<int>("a"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [0, 2, 0, 0] : [2, 2, unchecked(a[1] + 1), unchecked(a[1] + 1)]);

    internal static readonly DifferentialCase s_compoundAssignment = Case(
        nameof(ConditionalAccessDifferentialKernels.CompoundAssignmentKernel),
        ["slot", "right_side_evaluations"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchCompoundAssignmentKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_readAndWriteOrder = Case(
        nameof(ConditionalAccessDifferentialKernels.ReadAndWriteOrderKernel),
        ["slot", "read_then_write", "write_then_read"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchReadAndWriteOrderKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_accessInOtherBranch = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.SuspendingCallInOtherBranchKernel) + "Access",
        ["call_evaluations", "index_evaluations", "value"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchSuspendingCallInOtherBranchKernel(
            b.Get<int>("a"), b.Get<int>("dst"), 0, gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [0, 0, -1] : [0, 1, a[1]]);

    internal static readonly DifferentialCase s_callInOtherBranch = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.SuspendingCallInOtherBranchKernel) + "Call",
        ["call_evaluations", "index_evaluations", "value"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchSuspendingCallInOtherBranchKernel(
            b.Get<int>("a"), b.Get<int>("dst"), 2, gridDimX: s.GridX, blockDimX: s.BlockX),
        static (_, _) => [1, 0, 1]);

    internal static readonly DifferentialCase s_computedMember = Case(
        nameof(ConditionalAccessDifferentialKernels.ComputedMemberKernel),
        ["twice", "inner"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchComputedMemberKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_holderWithoutData = Case(
        nameof(ConditionalAccessDifferentialKernels.HolderParameterKernel) + "WithoutData",
        ["length_or_default"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchHolderParameterKernel(
            new ConditionalHolder { Data = null }, b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_holderWithData = Case(
        nameof(ConditionalAccessDifferentialKernels.HolderParameterKernel) + "WithData",
        ["length_or_default"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchHolderParameterKernel(
            new ConditionalHolder { Data = b.Get<int>("a") }, b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_extensionCall = Case(
        nameof(ConditionalAccessDifferentialKernels.ExtensionCallKernel),
        ["has_value", "sum"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchExtensionCallKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_OnAnArray_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_array);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_OnANullableStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableStruct);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_Chained_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_chain);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ReceiverWithASideEffect_IsEvaluatedOnce() =>
        DifferentialRunner.Run(s_receiverOnce);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_Assignment_EvaluatesTheRightSideOnlyForANonNullReceiver() =>
        DifferentialRunner.Run(s_assignment);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_OnAStackArray_AlwaysHasAValue() => DifferentialRunner.Run(s_stackArray);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_SuspendingReceiver_IsEvaluatedOnceForEveryRow() =>
        DifferentialRunner.Run(s_suspendingReceiver);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_BeforeABarrier_EvaluatesTheIndexOnlyForANonNullReceiver() =>
        DifferentialRunner.Run(s_accessBeforeBarrier);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_InALoopWithABarrier_PassesTheFrameVariableByReference() =>
        DifferentialRunner.Run(s_accessInBarrierLoop);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_NextToASuspendingCall_KeepsTheOrderOfEvaluation() =>
        DifferentialRunner.Run(s_suspendingCallBesideAccess);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_CompoundAssignment_EvaluatesTheRightSideOnlyForANonNullReceiver() =>
        DifferentialRunner.Run(s_compoundAssignment);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ReadAndWriteOfOneElement_KeepsTheOrderOfEvaluation() =>
        DifferentialRunner.Run(s_readAndWriteOrder);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_InTheArmTakenBesideASuspendingArm_EvaluatesOnlyTheAccess() =>
        DifferentialRunner.Run(s_accessInOtherBranch);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_InTheArmNotTakenBesideASuspendingArm_EvaluatesOnlyTheCall() =>
        DifferentialRunner.Run(s_callInOtherBranch);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ComputedAndNestedMemberOfANullableStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_computedMember);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_NullArrayFieldOfAStructParameter_TakesTheDefault() =>
        DifferentialRunner.Run(s_holderWithoutData);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ArrayFieldOfAStructParameter_ReadsTheLength() =>
        DifferentialRunner.Run(s_holderWithData);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ExtensionMethodCall_CallsOnlyForANonNullReceiver() =>
        DifferentialRunner.Run(s_extensionCall);
}
