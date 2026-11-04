#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>Gpu.ForUnroll</c>, device iterators (<c>yield return</c> / <c>yield break</c>),
/// local functions, <c>using</c> disposal lifecycle, <c>MemoryExtensions.Sort</c>,
/// <c>[InlineArray]</c>, <c>fixed</c> buffers, collection expressions, named/optional arguments, and <c>ref</c> returns.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class LanguageControlAndResourceDifferentialTests
{
    private const int Rows = 128;

    private static DifferentialCase CreateControlCase(
        string kernelName,
        string column,
        Action<int[], int[], int[], uint, uint> launch,
        Action<int[], int[], int[]> oracle) =>
        new($"{nameof(LanguageControlAndResourceDifferentialKernels)}.{kernelName}")
        {
            Shape = LaunchShape.ForRows(Rows, blockX: 64),
            Inputs = (rng, rows) =>
            {
                var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers()
                    .In("xs", xs)
                    .In("ys", ys)
                    .Out<int>("r", rows, [column], OutputPolicy.Exact);
            },
            Launch = (b, s) => launch(
                b.Get<int>("xs"),
                b.Get<int>("ys"),
                b.Get<int>("r"),
                s.GridX,
                s.BlockX),
            Oracle = (b, _) => oracle(
                b.Get<int>("xs"),
                b.Get<int>("ys"),
                b.Get<int>("r")),
        };

    // ---------------------------------------------------------
    // ForUnroll and Device Iterators Cases
    // ---------------------------------------------------------

    private static readonly DifferentialCase s_forUnrollPositiveRange = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.ForUnrollPositiveRangeKernel),
        "unroll_pos_sum",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchForUnrollPositiveRangeKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.ForUnrollPositiveRangeKernel(xs, ys, r));

    private static readonly DifferentialCase s_forUnrollNegativeStart = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.ForUnrollNegativeStartKernel),
        "unroll_neg_start",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchForUnrollNegativeStartKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.ForUnrollNegativeStartKernel(xs, ys, r));

    private static readonly DifferentialCase s_forUnrollZeroCount = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.ForUnrollZeroCountKernel),
        "unroll_zero_count_untouched",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchForUnrollZeroCountKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.ForUnrollZeroCountKernel(xs, ys, r));

    private static readonly DifferentialCase s_forUnrollRefContext = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.ForUnrollRefContextKernel),
        "unroll_ref_ctx_acc",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchForUnrollRefContextKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.ForUnrollRefContextKernel(xs, ys, r));

    private static readonly DifferentialCase s_forUnrollNested = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.ForUnrollNestedKernel),
        "unroll_nested_acc",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchForUnrollNestedKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.ForUnrollNestedKernel(xs, ys, r));

    private static readonly DifferentialCase s_deviceIteratorEarlyBreak = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.DeviceIteratorEarlyBreakKernel),
        "iter_early_break_sum",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorEarlyBreakKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.DeviceIteratorEarlyBreakKernel(xs, ys, r));

    private static readonly DifferentialCase s_deviceIteratorConsecutiveExpansions = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.DeviceIteratorConsecutiveExpansionsKernel),
        "iter_two_consecutive_expansions",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorConsecutiveExpansionsKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.DeviceIteratorConsecutiveExpansionsKernel(xs, ys, r));

    private static readonly DifferentialCase s_deviceIteratorConsumerBreakContinue = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.DeviceIteratorConsumerBreakContinueKernel),
        "iter_consumer_break_continue",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorConsumerBreakContinueKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.DeviceIteratorConsumerBreakContinueKernel(xs, ys, r));

    private static readonly DifferentialCase s_deviceIteratorNestedExpansion = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.DeviceIteratorNestedExpansionKernel),
        "iter_nested_expansion",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorNestedExpansionKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.DeviceIteratorNestedExpansionKernel(xs, ys, r));

    private static readonly DifferentialCase s_deviceIteratorOptionalParamDefault = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.DeviceIteratorOptionalParamDefaultKernel),
        "iter_optional_param_default",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorOptionalParamDefaultKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.DeviceIteratorOptionalParamDefaultKernel(xs, ys, r));

    // ---------------------------------------------------------
    // Local Functions and Using Lifecycle Cases
    // ---------------------------------------------------------

    private static readonly DifferentialCase s_staticLocalFunction = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.StaticLocalFunctionKernel),
        "static_local_fn",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchStaticLocalFunctionKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.StaticLocalFunctionKernel(xs, ys, r));

    private static readonly DifferentialCase s_capturingLocalFunctionReturn = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.CapturingLocalFunctionReturnKernel),
        "capturing_local_fn_ret",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchCapturingLocalFunctionReturnKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.CapturingLocalFunctionReturnKernel(xs, ys, r));

    private static readonly DifferentialCase s_capturingLocalFunctionMutatedOuter = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.CapturingLocalFunctionMutatedOuterKernel),
        "capturing_local_fn_mutated_outer",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchCapturingLocalFunctionMutatedOuterKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.CapturingLocalFunctionMutatedOuterKernel(xs, ys, r));

    private static readonly DifferentialCase s_refOutLocalFunctionState = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.RefOutLocalFunctionStateKernel),
        "ref_out_local_fn_state",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchRefOutLocalFunctionStateKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.RefOutLocalFunctionStateKernel(xs, ys, r));

    private static readonly DifferentialCase s_refOutLocalFunctionSnapshot = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.RefOutLocalFunctionSnapshotKernel),
        "ref_out_local_fn_snap",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchRefOutLocalFunctionSnapshotKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.RefOutLocalFunctionSnapshotKernel(xs, ys, r));

    private static readonly DifferentialCase s_blockLambdaReturn = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.BlockLambdaReturnKernel),
        "block_lambda_ret",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchBlockLambdaReturnKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.BlockLambdaReturnKernel(xs, ys, r));

    private static readonly DifferentialCase s_usingLifoOrder = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.UsingLifoOrderKernel),
        "using_lifo_321",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchUsingLifoOrderKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.UsingLifoOrderKernel(xs, ys, r));

    private static readonly DifferentialCase s_usingEarlyReturnAndOrder = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.UsingEarlyReturnAndOrderKernel),
        "using_early_return_and_order",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchUsingEarlyReturnAndOrderKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.UsingEarlyReturnAndOrderKernel(xs, ys, r));

    private static readonly DifferentialCase s_usingLoopBreakContinueTrace = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.UsingLoopBreakContinueTraceKernel),
        "using_loop_break_continue_trace",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchUsingLoopBreakContinueTraceKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.UsingLoopBreakContinueTraceKernel(xs, ys, r));

    private static readonly DifferentialCase s_usingPatternRefStruct = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.UsingPatternRefStructKernel),
        "using_pattern_ref_struct",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchUsingPatternRefStructKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.UsingPatternRefStructKernel(xs, ys, r));

    private static readonly DifferentialCase s_usingMutatingGuardGetterVal = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.UsingMutatingGuardGetterValKernel),
        "using_mut_guard_getter_val",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchUsingMutatingGuardGetterValKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.UsingMutatingGuardGetterValKernel(xs, ys, r));

    private static readonly DifferentialCase s_usingMutatingGuardDisposedState = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.UsingMutatingGuardDisposedStateKernel),
        "using_mut_guard_disposed_state",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchUsingMutatingGuardDisposedStateKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.UsingMutatingGuardDisposedStateKernel(xs, ys, r));

    // ---------------------------------------------------------
    // SpanSort and Modern Syntax Cases
    // ---------------------------------------------------------

    private static readonly DifferentialCase s_spanSortInt = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortIntKernel),
        "sort_int_asc",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortIntKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortIntKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortEnum = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortEnumKernel),
        "sort_enum_signed",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortEnumKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortEnumKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortComparableStruct = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortComparableStructKernel),
        "sort_comparable_struct_payloads",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortComparableStructKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortComparableStructKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortComparer = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortComparerKernel),
        "sort_comparer_desc",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortComparerKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortComparerKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortMethodGroupComparison = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortMethodGroupComparisonKernel),
        "sort_method_group_comparison",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortMethodGroupComparisonKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortMethodGroupComparisonKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortKeyValueDefault = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortKeyValueDefaultKernel),
        "sort_kv_default_items",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortKeyValueDefaultKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortKeyValueDefaultKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortKeyValueComparer = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortKeyValueComparerKernel),
        "sort_kv_comparer_items",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortKeyValueComparerKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortKeyValueComparerKernel(xs, ys, r));

    private static readonly DifferentialCase s_spanSortKeyValueLambda = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.SpanSortKeyValueLambdaKernel),
        "sort_kv_lambda_items",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortKeyValueLambdaKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.SpanSortKeyValueLambdaKernel(xs, ys, r));

    private static readonly DifferentialCase s_inlineArrayAndFixedBuffer = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.InlineArrayAndFixedBufferKernel),
        "inline_array_and_fixed_buf",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchInlineArrayAndFixedBufferKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.InlineArrayAndFixedBufferKernel(xs, ys, r));

    private static readonly DifferentialCase s_collectionExpressionSpread = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.CollectionExpressionSpreadKernel),
        "collection_expr_spread",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchCollectionExpressionSpreadKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.CollectionExpressionSpreadKernel(xs, ys, r));

    private static readonly DifferentialCase s_namedArgumentsEvaluationOrder = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.NamedArgumentsEvaluationOrderKernel),
        "named_args_val_and_eval_order",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchNamedArgumentsEvaluationOrderKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.NamedArgumentsEvaluationOrderKernel(xs, ys, r));

    private static readonly DifferentialCase s_optionalArgumentsAndRefReturn = CreateControlCase(
        nameof(LanguageControlAndResourceDifferentialKernels.OptionalArgumentsAndRefReturnKernel),
        "optional_args_and_ref_return",
        static (xs, ys, r, gx, bx) => LanguageControlAndResourceDifferentialKernels.LaunchOptionalArgumentsAndRefReturnKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        static (xs, ys, r) => LanguageControlAndResourceDifferentialKernels.OptionalArgumentsAndRefReturnKernel(xs, ys, r));

    // ---------------------------------------------------------
    // Test Methods
    // ---------------------------------------------------------

    [SkippableFact]
    public void Test_ForUnrollPositiveRange_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollPositiveRange);

    [SkippableFact]
    public void Test_ForUnrollNegativeStart_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollNegativeStart);

    [SkippableFact]
    public void Test_ForUnrollZeroCount_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollZeroCount);

    [SkippableFact]
    public void Test_ForUnrollRefContext_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollRefContext);

    [SkippableFact]
    public void Test_ForUnrollNested_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollNested);

    [SkippableFact]
    public void Test_DeviceIteratorEarlyBreak_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorEarlyBreak);

    [SkippableFact]
    public void Test_DeviceIteratorConsecutiveExpansions_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorConsecutiveExpansions);

    [SkippableFact]
    public void Test_DeviceIteratorConsumerBreakContinue_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorConsumerBreakContinue);

    [SkippableFact]
    public void Test_DeviceIteratorNestedExpansion_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorNestedExpansion);

    [SkippableFact]
    public void Test_DeviceIteratorOptionalParamDefault_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorOptionalParamDefault);

    [SkippableFact]
    public void Test_StaticLocalFunction_MatchesTheOracle() =>
        DifferentialRunner.Run(s_staticLocalFunction);

    [SkippableFact]
    public void Test_CapturingLocalFunctionReturn_MatchesTheOracle() =>
        DifferentialRunner.Run(s_capturingLocalFunctionReturn);

    [SkippableFact]
    public void Test_CapturingLocalFunctionMutatedOuter_MatchesTheOracle() =>
        DifferentialRunner.Run(s_capturingLocalFunctionMutatedOuter);

    [SkippableFact]
    public void Test_RefOutLocalFunctionState_MatchesTheOracle() =>
        DifferentialRunner.Run(s_refOutLocalFunctionState);

    [SkippableFact]
    public void Test_RefOutLocalFunctionSnapshot_MatchesTheOracle() =>
        DifferentialRunner.Run(s_refOutLocalFunctionSnapshot);

    [SkippableFact]
    public void Test_BlockLambdaReturn_MatchesTheOracle() =>
        DifferentialRunner.Run(s_blockLambdaReturn);

    [SkippableFact]
    public void Test_UsingLifoOrder_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingLifoOrder);

    [SkippableFact]
    public void Test_UsingEarlyReturnAndOrder_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingEarlyReturnAndOrder);

    [SkippableFact]
    public void Test_UsingLoopBreakContinueTrace_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingLoopBreakContinueTrace);

    [SkippableFact]
    public void Test_UsingPatternRefStruct_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingPatternRefStruct);

    [SkippableFact]
    public void Test_UsingMutatingGuardGetterVal_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingMutatingGuardGetterVal);

    [SkippableFact]
    public void Test_UsingMutatingGuardDisposedState_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingMutatingGuardDisposedState);

    [SkippableFact]
    public void Test_SpanSortInt_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortInt);

    [SkippableFact]
    public void Test_SpanSortEnum_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortEnum);

    [SkippableFact]
    public void Test_SpanSortComparableStruct_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortComparableStruct);

    [SkippableFact]
    public void Test_SpanSortComparer_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortComparer);

    [SkippableFact]
    public void Test_SpanSortMethodGroupComparison_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortMethodGroupComparison);

    [SkippableFact]
    public void Test_SpanSortKeyValueDefault_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortKeyValueDefault);

    [SkippableFact]
    public void Test_SpanSortKeyValueComparer_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortKeyValueComparer);

    [SkippableFact]
    public void Test_SpanSortKeyValueLambda_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortKeyValueLambda);

    [SkippableFact]
    public void Test_InlineArrayAndFixedBuffer_MatchesTheOracle() =>
        DifferentialRunner.Run(s_inlineArrayAndFixedBuffer);

    [SkippableFact]
    public void Test_CollectionExpressionSpread_MatchesTheOracle() =>
        DifferentialRunner.Run(s_collectionExpressionSpread);

    [SkippableFact]
    public void Test_NamedArgumentsEvaluationOrder_MatchesTheOracle() =>
        DifferentialRunner.Run(s_namedArgumentsEvaluationOrder);

    [SkippableFact]
    public void Test_OptionalArgumentsAndRefReturn_MatchesTheOracle() =>
        DifferentialRunner.Run(s_optionalArgumentsAndRefReturn);
}
