#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>Differential tests of the null-conditional access <c>?.</c>, one case per shape.</summary>
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
}
