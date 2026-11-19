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
        Action<DiffBuffers, LaunchShape> launch, Func<int[], int, int[]> expected, bool qualified = false) =>
        new(qualified ? kernel : $"{nameof(ConditionalAccessDifferentialKernels)}.{kernel}")
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

    // A kernel that reads a second input array b
    private static DifferentialCase CaseWithSecondInput(string kernel, string[] columns,
        Action<DiffBuffers, LaunchShape> launch) =>
        new($"{nameof(ConditionalAccessDifferentialKernels)}.{kernel}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .In("b", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            Launch = launch,
            OracleIsLaunch = true,
        };

    private static bool IsNullRow(int[] a, int t) => (a[t] & 1) == 0;

    // Fresh for each launch: the kernels write into them
    private static ConditionalPoint[] Points(int rows)
    {
        var points = new ConditionalPoint[rows];
        for (int i = 0; i < rows; i++)
        {
            points[i] = new ConditionalPoint { X = i, Y = 2 * i };
        }

        return points;
    }

    // Inner.X = 3 * i
    private static ConditionalComputed[] Computed(int rows)
    {
        var values = new ConditionalComputed[rows];
        for (int i = 0; i < rows; i++)
        {
            values[i] = new ConditionalComputed { X = i, Inner = new ConditionalPoint { X = 3 * i, Y = 0 } };
        }

        return values;
    }

    // Every third element null
    private static ConditionalPoint?[] NullablePoints(int rows)
    {
        var points = new ConditionalPoint?[rows];
        for (int i = 0; i < rows; i++)
        {
            points[i] = i % 3 == 0 ? null : new ConditionalPoint { X = i + 100, Y = 0 };
        }

        return points;
    }

    // Every other element null
    private static int?[] NullableInts(int rows)
    {
        var values = new int?[rows];
        for (int i = 0; i < rows; i++)
        {
            values[i] = i % 2 == 0 ? null : i;
        }

        return values;
    }

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

    internal static readonly DifferentialCase s_nestedElementReceiver = Case(
        nameof(ConditionalAccessDifferentialKernels.NestedElementReceiverKernel),
        ["has_value", "x"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchNestedElementReceiverKernel(
            b.Get<int>("a"), NullablePoints(s.Rows), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_coalesceAssignmentElement = Case(
        nameof(ConditionalAccessDifferentialKernels.CoalesceAssignmentElementKernel),
        ["element", "right_side_evaluations"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchCoalesceAssignmentElementKernel(
            b.Get<int>("a"), NullableInts(s.Rows), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_extensionOnBinding = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.ExtensionOnBindingKernel),
        ["element_plus_one", "x_after_increment", "field_of_element_plus_one", "inner_plus_one"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchExtensionOnBindingKernel(
            b.Get<int>("a"), Points(s.Rows), Computed(s.Rows), b.Get<int>("dst"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) =>
        [
            IsNullRow(a, t) ? -1 : t + 1,
            IsNullRow(a, t) ? t : t + 1,
            IsNullRow(a, t) ? -1 : 3 * t + 1,
            (a[t] & 2) == 0 ? -1 : (a[t] & 0xFF) + 1
        ]);

    internal static readonly DifferentialCase s_nestedExtensionCall = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.NestedExtensionCallKernel),
        ["holder_data_sum", "holder_log", "element_plus_one", "index_log"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchNestedExtensionCallKernel(
            b.Get<int>("a"), NullablePoints(s.Rows), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) =>
        [
            (a[t] & 3) < 2 ? -1 : unchecked(a[0] + a[1]),
            1,
            IsNullRow(a, t) || t % 3 == 0 ? -1 : t + 101,
            IsNullRow(a, t) ? 0 : 2
        ]);

    internal static readonly DifferentialCase s_voidExtensionStatement = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.VoidExtensionStatementKernel),
        ["chain_written", "element_of_chain_x", "unrolled_x", "device_function_x", "local_function_x"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchVoidExtensionStatementKernel(
            b.Get<int>("a"), new int[s.Rows], Points(s.Rows), Points(s.Rows), Points(s.Rows), Points(s.Rows),
            b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) =>
        [
            (a[t] & 3) < 2 ? 0 : 7,
            (a[t] & 4) == 0 ? t : t + 1,
            IsNullRow(a, t) ? t : t + 2,
            (a[t] & 8) == 0 ? t : t + 1,
            (a[t] & 16) == 0 ? t : t + 1
        ]);

    internal static readonly DifferentialCase s_twinFirst = BarrierCase(
        nameof(ConditionalAccessTwinKernels) + "." + nameof(ConditionalAccessTwinKernels.TwinFirstFileKernel),
        ["overload", "same_signature"],
        static (b, s) => ConditionalAccessTwinKernels.LaunchTwinFirstFileKernel(
            b.Get<int>("a"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [-1, -1] : [unchecked(a[0] + 1000), unchecked(a[2] + 3000)],
        qualified: true);

    internal static readonly DifferentialCase s_twinSecond = BarrierCase(
        nameof(ConditionalAccessTwinKernels) + "." + nameof(ConditionalAccessTwinKernels.TwinSecondFileKernel),
        ["overload", "same_signature"],
        static (b, s) => ConditionalAccessTwinKernels.LaunchTwinSecondFileKernel(
            b.Get<int>("a"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        static (a, t) => IsNullRow(a, t) ? [-1, -1] : [unchecked(a[1] + 2000), unchecked(a[3] + 4000)],
        qualified: true);

    internal static readonly DifferentialCase s_receiverReassigned = CaseWithSecondInput(
        nameof(ConditionalAccessDifferentialKernels.ReceiverReassignedKernel),
        ["element", "first_after_swap"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchReceiverReassignedKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ReceiverIsANullableElement_ReadsItsValueOnlyWhenItHasOne() =>
        DifferentialRunner.Run(s_nestedElementReceiver);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_CoalesceAssignmentOfAnElement_EvaluatesTheRightSideOnlyForANullElement() =>
        DifferentialRunner.Run(s_coalesceAssignmentElement);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ExtensionCallOnABinding_CallsOnlyForANonNullReceiver() =>
        DifferentialRunner.Run(s_extensionOnBinding);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_IndexReassignsTheReceiver_ReadsTheReceiverEvaluatedFirst() =>
        DifferentialRunner.Run(s_receiverReassigned);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ExtensionCallInTheFirstFileOfAPartialClass_CallsTheMethodThatFileBinds() =>
        DifferentialRunner.Run(s_twinFirst);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ExtensionCallInTheSecondFileOfAPartialClass_CallsTheMethodThatFileBinds() =>
        DifferentialRunner.Run(s_twinSecond);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_ExtensionCallOfAnAccessOnABinding_EvaluatesEachReceiverOnceInOrder() =>
        DifferentialRunner.Run(s_nestedExtensionCall);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_VoidExtensionCallAsAStatement_RunsOnlyForNonNullReceivers() =>
        DifferentialRunner.Run(s_voidExtensionStatement);
}
