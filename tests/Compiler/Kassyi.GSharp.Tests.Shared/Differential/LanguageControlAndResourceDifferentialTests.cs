#nullable enable
using System;
using System.Runtime.CompilerServices;
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
        Int32TripletLaunch launch,
        string column,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) =>
            {
                var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers()
                    .In("xs", xs)
                    .In("ys", ys)
                    .Out<int>("r", rows, [column], OutputPolicy.Exact);
            },
            shape: LaunchShape.ForRows(Rows, blockX: 64),
            firstInputName: "xs",
            secondInputName: "ys",
            outputName: "r",
            launchExpression: launchExpression);

    // ---------------------------------------------------------
    // ForUnroll and Device Iterators Cases
    // ---------------------------------------------------------

    internal static readonly DifferentialCase s_forUnrollPositiveRange = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchForUnrollPositiveRangeKernel,
        "unroll_pos_sum");

    internal static readonly DifferentialCase s_forUnrollNegativeStart = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchForUnrollNegativeStartKernel,
        "unroll_neg_start");

    internal static readonly DifferentialCase s_forUnrollZeroCount = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchForUnrollZeroCountKernel,
        "unroll_zero_count_untouched");

    internal static readonly DifferentialCase s_forUnrollRefContext = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchForUnrollRefContextKernel,
        "unroll_ref_ctx_acc");

    internal static readonly DifferentialCase s_forUnrollNested = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchForUnrollNestedKernel,
        "unroll_nested_acc");

    internal static readonly DifferentialCase s_deviceIteratorEarlyBreak = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorEarlyBreakKernel,
        "iter_early_break_sum");

    internal static readonly DifferentialCase s_deviceIteratorConsecutiveExpansions = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorConsecutiveExpansionsKernel,
        "iter_two_consecutive_expansions");

    internal static readonly DifferentialCase s_deviceIteratorConsumerBreakContinue = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorConsumerBreakContinueKernel,
        "iter_consumer_break_continue");

    internal static readonly DifferentialCase s_deviceIteratorNestedExpansion = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorNestedExpansionKernel,
        "iter_nested_expansion");

    internal static readonly DifferentialCase s_deviceIteratorOptionalParamDefault = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchDeviceIteratorOptionalParamDefaultKernel,
        "iter_optional_param_default");

    // ---------------------------------------------------------
    // Local Functions and Using Lifecycle Cases
    // ---------------------------------------------------------

    internal static readonly DifferentialCase s_staticLocalFunction = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchStaticLocalFunctionKernel,
        "static_local_fn");

    internal static readonly DifferentialCase s_capturingLocalFunctionReturn = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchCapturingLocalFunctionReturnKernel,
        "capturing_local_fn_ret");

    internal static readonly DifferentialCase s_capturingLocalFunctionMutatedOuter = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchCapturingLocalFunctionMutatedOuterKernel,
        "capturing_local_fn_mutated_outer");

    internal static readonly DifferentialCase s_refOutLocalFunctionState = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchRefOutLocalFunctionStateKernel,
        "ref_out_local_fn_state");

    internal static readonly DifferentialCase s_refOutLocalFunctionSnapshot = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchRefOutLocalFunctionSnapshotKernel,
        "ref_out_local_fn_snap");

    internal static readonly DifferentialCase s_blockLambdaReturn = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchBlockLambdaReturnKernel,
        "block_lambda_ret");

    internal static readonly DifferentialCase s_usingLifoOrder = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchUsingLifoOrderKernel,
        "using_lifo_321");

    internal static readonly DifferentialCase s_usingEarlyReturnAndOrder = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchUsingEarlyReturnAndOrderKernel,
        "using_early_return_and_order");

    internal static readonly DifferentialCase s_usingLoopBreakContinueTrace = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchUsingLoopBreakContinueTraceKernel,
        "using_loop_break_continue_trace");

    internal static readonly DifferentialCase s_usingPatternRefStruct = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchUsingPatternRefStructKernel,
        "using_pattern_ref_struct");

    internal static readonly DifferentialCase s_usingMutatingGuardGetterVal = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchUsingMutatingGuardGetterValKernel,
        "using_mut_guard_getter_val");

    internal static readonly DifferentialCase s_usingMutatingGuardDisposedState = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchUsingMutatingGuardDisposedStateKernel,
        "using_mut_guard_disposed_state");

    // ---------------------------------------------------------
    // SpanSort and Modern Syntax Cases
    // ---------------------------------------------------------

    internal static readonly DifferentialCase s_spanSortInt = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortIntKernel,
        "sort_int_asc");

    internal static readonly DifferentialCase s_spanSortEnum = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortEnumKernel,
        "sort_enum_signed");

    internal static readonly DifferentialCase s_spanSortComparableStruct = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortComparableStructKernel,
        "sort_comparable_struct_payloads");

    internal static readonly DifferentialCase s_spanSortComparer = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortComparerKernel,
        "sort_comparer_desc");

    internal static readonly DifferentialCase s_spanSortMethodGroupComparison = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortMethodGroupComparisonKernel,
        "sort_method_group_comparison");

    internal static readonly DifferentialCase s_spanSortKeyValueDefault = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortKeyValueDefaultKernel,
        "sort_kv_default_items");

    internal static readonly DifferentialCase s_spanSortKeyValueComparer = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortKeyValueComparerKernel,
        "sort_kv_comparer_items");

    internal static readonly DifferentialCase s_spanSortKeyValueLambda = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchSpanSortKeyValueLambdaKernel,
        "sort_kv_lambda_items");

    internal static readonly DifferentialCase s_inlineArrayAndFixedBuffer = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchInlineArrayAndFixedBufferKernel,
        "inline_array_and_fixed_buf");

    internal static readonly DifferentialCase s_collectionExpressionSpread = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchCollectionExpressionSpreadKernel,
        "collection_expr_spread");

    internal static readonly DifferentialCase s_namedArgumentsEvaluationOrder = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchNamedArgumentsEvaluationOrderKernel,
        "named_args_val_and_eval_order");

    internal static readonly DifferentialCase s_optionalArgumentsAndRefReturn = CreateControlCase(
        LanguageControlAndResourceDifferentialKernels.LaunchOptionalArgumentsAndRefReturnKernel,
        "optional_args_and_ref_return");

    // ---------------------------------------------------------
    // Test Methods
    // ---------------------------------------------------------

    [SkippableFact]
    public void ForUnroll_PositiveRange_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollPositiveRange);

    [SkippableFact]
    public void ForUnroll_NegativeStart_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollNegativeStart);

    [SkippableFact]
    public void ForUnroll_ZeroCount_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollZeroCount);

    [SkippableFact]
    public void ForUnroll_RefContext_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollRefContext);

    [SkippableFact]
    public void ForUnroll_Nested_MatchesTheOracle() =>
        DifferentialRunner.Run(s_forUnrollNested);

    [SkippableFact]
    public void DeviceIterator_EarlyBreak_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorEarlyBreak);

    [SkippableFact]
    public void DeviceIterator_ConsecutiveExpansions_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorConsecutiveExpansions);

    [SkippableFact]
    public void DeviceIterator_ConsumerBreakContinue_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorConsumerBreakContinue);

    [SkippableFact]
    public void DeviceIterator_NestedExpansion_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorNestedExpansion);

    [SkippableFact]
    public void DeviceIterator_OptionalParamDefault_MatchesTheOracle() =>
        DifferentialRunner.Run(s_deviceIteratorOptionalParamDefault);

    [SkippableFact]
    public void LocalFunction_Static_MatchesTheOracle() =>
        DifferentialRunner.Run(s_staticLocalFunction);

    [SkippableFact]
    public void LocalFunction_CapturingReturn_MatchesTheOracle() =>
        DifferentialRunner.Run(s_capturingLocalFunctionReturn);

    [SkippableFact]
    public void LocalFunction_CapturingMutatedOuter_MatchesTheOracle() =>
        DifferentialRunner.Run(s_capturingLocalFunctionMutatedOuter);

    [SkippableFact]
    public void LocalFunction_RefOutState_MatchesTheOracle() =>
        DifferentialRunner.Run(s_refOutLocalFunctionState);

    [SkippableFact]
    public void LocalFunction_RefOutSnapshot_MatchesTheOracle() =>
        DifferentialRunner.Run(s_refOutLocalFunctionSnapshot);

    [SkippableFact]
    public void BlockLambda_Return_MatchesTheOracle() =>
        DifferentialRunner.Run(s_blockLambdaReturn);

    [SkippableFact]
    public void UsingStatement_LifoOrder_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingLifoOrder);

    [SkippableFact]
    public void UsingStatement_EarlyReturnAndOrder_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingEarlyReturnAndOrder);

    [SkippableFact]
    public void UsingStatement_LoopBreakContinueTrace_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingLoopBreakContinueTrace);

    [SkippableFact]
    public void UsingStatement_PatternRefStruct_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingPatternRefStruct);

    [SkippableFact]
    public void UsingStatement_MutatingGuardGetterVal_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingMutatingGuardGetterVal);

    [SkippableFact]
    public void UsingStatement_MutatingGuardDisposedState_MatchesTheOracle() =>
        DifferentialRunner.Run(s_usingMutatingGuardDisposedState);

    [SkippableFact]
    public void SpanSort_Int32Elements_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortInt);

    [SkippableFact]
    public void SpanSort_EnumElements_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortEnum);

    [SkippableFact]
    public void SpanSort_ComparableStruct_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortComparableStruct);

    [SkippableFact]
    public void SpanSort_CustomComparer_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortComparer);

    [SkippableFact]
    public void SpanSort_MethodGroupComparison_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortMethodGroupComparison);

    [SkippableFact]
    public void SpanSort_KeyValueDefault_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortKeyValueDefault);

    [SkippableFact]
    public void SpanSort_KeyValueComparer_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortKeyValueComparer);

    [SkippableFact]
    public void SpanSort_KeyValueLambda_MatchesTheOracle() =>
        DifferentialRunner.Run(s_spanSortKeyValueLambda);

    [SkippableFact]
    public void InlineArray_FixedBuffer_MatchesTheOracle() =>
        DifferentialRunner.Run(s_inlineArrayAndFixedBuffer);

    [SkippableFact]
    public void CollectionExpression_SpreadOperator_MatchesTheOracle() =>
        DifferentialRunner.Run(s_collectionExpressionSpread);

    [SkippableFact]
    public void NamedArguments_EvaluationOrder_MatchesTheOracle() =>
        DifferentialRunner.Run(s_namedArgumentsEvaluationOrder);

    [SkippableFact]
    public void OptionalArguments_RefReturn_MatchesTheOracle() =>
        DifferentialRunner.Run(s_optionalArgumentsAndRefReturn);
}
