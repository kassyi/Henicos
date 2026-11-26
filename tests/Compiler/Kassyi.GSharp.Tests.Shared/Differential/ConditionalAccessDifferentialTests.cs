#nullable enable
using System.Runtime.CompilerServices;
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

    private static DifferentialCase Case(
        Int32RowsLaunch launch,
        string[] columns,
        string? suffix = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            shapes: LaunchShapes.OneDimensionalMatrix(Rows),
            suffix: suffix,
            launchExpression: launchExpression);

    private static DifferentialCase Case(string kernel, string[] columns, Action<DiffBuffers, LaunchShape> launch) =>
        new(DiffKernelNames.Of(typeof(ConditionalAccessDifferentialKernels), kernel))
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            Launch = launch,
            OracleIsLaunch = true,
        };

    // A kernel with a barrier: expected gives the columns of a row from the input and the row
    private static DifferentialCase BarrierCase(
        Int32PairLaunch launch,
        string[] columns,
        Func<int[], int, int[]> expected,
        string? suffix = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, columns, OutputPolicy.Exact),
            shape: LaunchShape.ForRows(128, blockX: 64),
            suffix: suffix,
            reference: (b, _) =>
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
            launchExpression: launchExpression);

    private static DifferentialCase BarrierCase(
        string kernel,
        string[] columns,
        Action<DiffBuffers, LaunchShape> launch,
        Func<int[], int, int[]> expected) =>
        new(DiffKernelNames.Of(typeof(ConditionalAccessDifferentialKernels), kernel))
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
        ConditionalAccessDifferentialKernels.LaunchArrayConditionalAccessKernel,
        ["length_has_value", "length", "element_has_value", "element", "length_or_default"]);

    internal static readonly DifferentialCase s_nullableStruct = Case(
        ConditionalAccessDifferentialKernels.LaunchNullableStructConditionalAccessKernel,
        ["x_has_value", "x", "y_has_value", "y", "sum_with_defaults"]);

    internal static readonly DifferentialCase s_chain = Case(
        ConditionalAccessDifferentialKernels.LaunchChainedConditionalAccessKernel,
        ["length_has_value", "length", "first_has_value", "first", "length_or_default"]);

    internal static readonly DifferentialCase s_receiverOnce = Case(
        ConditionalAccessDifferentialKernels.LaunchReceiverEvaluatedOnceKernel,
        ["receiver_evaluations", "x", "y"]);

    internal static readonly DifferentialCase s_assignment = Case(
        ConditionalAccessDifferentialKernels.LaunchConditionalAssignmentKernel,
        ["assigned_or_sentinel", "right_side_evaluations", "assignment_value", "assigned_value_slot"]);

    internal static readonly DifferentialCase s_stackArray = Case(
        ConditionalAccessDifferentialKernels.LaunchStackArrayConditionalAccessKernel,
        ["length", "element", "element_plus_one"]);

    internal static readonly DifferentialCase s_suspendingReceiver = BarrierCase(
        ConditionalAccessDifferentialKernels.LaunchSuspendingReceiverKernel,
        ["receiver_evaluations", "has_value", "x"],
        static (a, t) => IsNullRow(a, t) ? [1, 0, -1] : [1, 1, 7]);

    internal static readonly DifferentialCase s_accessBeforeBarrier = BarrierCase(
        ConditionalAccessDifferentialKernels.LaunchAccessBeforeBarrierKernel,
        ["index_evaluations", "has_value", "value"],
        static (a, t) => IsNullRow(a, t) ? [0, 0, -1] : [1, 1, a[1]]);

    internal static readonly DifferentialCase s_accessInBarrierLoop = BarrierCase(
        ConditionalAccessDifferentialKernels.LaunchAccessInBarrierLoopKernel,
        ["index_evaluations", "sum"],
        static (a, t) => IsNullRow(a, t) ? [0, -2] : [2, unchecked(a[0] + a[1])]);

    internal static readonly DifferentialCase s_suspendingCallBesideAccess = BarrierCase(
        ConditionalAccessDifferentialKernels.LaunchSuspendingCallBesideAccessKernel,
        ["index_evaluations", "call_evaluations", "access_then_call", "call_then_access"],
        static (a, t) => IsNullRow(a, t) ? [0, 2, 0, 0] : [2, 2, unchecked(a[1] + 1), unchecked(a[1] + 1)]);

    internal static readonly DifferentialCase s_compoundAssignment = Case(
        ConditionalAccessDifferentialKernels.LaunchCompoundAssignmentKernel,
        ["slot", "right_side_evaluations"]);

    internal static readonly DifferentialCase s_readAndWriteOrder = Case(
        ConditionalAccessDifferentialKernels.LaunchReadAndWriteOrderKernel,
        ["slot", "read_then_write", "write_then_read"]);

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
        ConditionalAccessDifferentialKernels.LaunchComputedMemberKernel,
        ["twice", "inner"]);

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
        ConditionalAccessDifferentialKernels.LaunchExtensionCallKernel,
        ["has_value", "sum"]);

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
        ["chain_written", "element_of_chain_x", "unrolled_x"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchVoidExtensionStatementKernel(
            inputValues: b.Get<int>("a"),
            targets: new VoidExtensionStatementTargets
            {
                ChainValues = new int[s.Rows],
                HolderPoints = Points(s.Rows),
                UnrollPoints = Points(s.Rows),
            },
            results: b.Get<int>("dst"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static (a, t) =>
        [
            (a[t] & 3) < 2 ? 0 : 7,
            (a[t] & 4) == 0 ? t : t + 1,
            IsNullRow(a, t) ? t : t + 2
        ]);

    internal static readonly DifferentialCase s_voidExtensionBody = BarrierCase(
        nameof(ConditionalAccessDifferentialKernels.VoidExtensionBodyKernel),
        ["device_function_x", "managed_version_x", "local_function_x", "lambda_x"],
        static (b, s) => ConditionalAccessDifferentialKernels.LaunchVoidExtensionBodyKernel(
            inputValues: b.Get<int>("a"),
            targets: new VoidExtensionBodyTargets
            {
                DeviceFunctionPoints = Points(s.Rows),
                ManagedVersionPoints = Points(s.Rows),
                LocalFunctionPoints = Points(s.Rows),
                SingleThreadLambdaPoints = Points(s.Rows),
            },
            results: b.Get<int>("dst"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static (a, t) =>
        [
            (a[t] & 1) == 0 ? t : t + 1,
            (a[t] & 2) == 0 ? t : t + t * 10 / ((a[t] & 7) + 1),
            (a[t] & 4) == 0 ? t : t + 1,
            t == 0 && (a[0] & 8) != 0 ? 1 : t
        ]);

    internal static readonly DifferentialCase s_twinFirst = BarrierCase(
        ConditionalAccessTwinKernels.LaunchTwinFirstFileKernel,
        ["overload", "same_signature"],
        static (a, t) => IsNullRow(a, t) ? [-1, -1] : [unchecked(a[0] + 1000), unchecked(a[2] + 3000)]);

    internal static readonly DifferentialCase s_twinSecond = BarrierCase(
        ConditionalAccessTwinKernels.LaunchTwinSecondFileKernel,
        ["overload", "same_signature"],
        static (a, t) => IsNullRow(a, t) ? [-1, -1] : [unchecked(a[1] + 2000), unchecked(a[3] + 4000)]);

    internal static readonly DifferentialCase s_receiverReassigned = DiffCases.Create(
        ConditionalAccessDifferentialKernels.LaunchReceiverReassignedKernel,
        inputs: (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("b", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["element", "first_after_swap"], OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(Rows));

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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalAccess_VoidExtensionCallAsAVoidBody_RunsOnlyForNonNullReceivers() =>
        DifferentialRunner.Run(s_voidExtensionBody);
}
