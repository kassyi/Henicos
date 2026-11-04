#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for C# generics, nested types, extension methods, reduced extension method groups,
/// tuple deconstruction, <c>readonly record struct</c>, and pattern matching.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class LanguageGenericsAndTypesDifferentialTests
{
    private const int Rows = 256;

    private static DifferentialCase CreateCase(
        string kernelName,
        string[] columns,
        Action<int[], int[], int[], uint, uint> launch,
        Action<int[], int[], int[]> oracle) =>
        new($"{nameof(LanguageGenericsAndTypesDifferentialKernels)}.{kernelName}")
        {
            Shape = LaunchShape.ForRows(Rows, blockX: 64),
            Inputs = (rng, rows) =>
            {
                var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers()
                    .In("xs", xs)
                    .In("ys", ys)
                    .Out<int>("r", rows, columns, OutputPolicy.Exact);
            },
            Launch = (b, s) => launch(b.Get<int>("xs"), b.Get<int>("ys"), b.Get<int>("r"), s.GridX, s.BlockX),
            Oracle = (b, _) => oracle(b.Get<int>("xs"), b.Get<int>("ys"), b.Get<int>("r")),
        };

    // 1. Generic Structs & Nested Types
    private static readonly DifferentialCase s_genScopeUsing = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.GenericStructScopeUsingKernel),
        ["int_box_val", "int_box_tag_plus_ten", "int_box_disposed_marker"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchGenericStructScopeUsingKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.GenericStructScopeUsingKernel);

    private static readonly DifferentialCase s_genPrimitivesAndNesting = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.GenericStructPrimitivesAndNestingKernel),
        ["uint_box_val", "short_box_val", "nested_pair_first_val", "nested_pair_first_tag", "nested_pair_second"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchGenericStructPrimitivesAndNestingKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.GenericStructPrimitivesAndNestingKernel);

    private static readonly DifferentialCase s_roWeightedSum = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.ReadonlyStructWeightedSumKernel),
        ["ro_cell_weighted_sum"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchReadonlyStructWeightedSumKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.ReadonlyStructWeightedSumKernel);

    private static readonly DifferentialCase s_nestedTypes = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.NestedTypesInNamespaceAndClassKernel),
        ["nested_scope_add", "nested_scope_xor", "nested_scope_scale"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchNestedTypesInNamespaceAndClassKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.NestedTypesInNamespaceAndClassKernel);

    // 2. Extension Methods & Delegates
    private static readonly DifferentialCase s_valExt = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.ValueReceiverExtensionMethodKernel),
        ["val_ext_ret", "val_ext_caller_unchanged"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchValueReceiverExtensionMethodKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.ValueReceiverExtensionMethodKernel);

    private static readonly DifferentialCase s_inExt = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.InReceiverExtensionMethodKernel),
        ["in_ext_ret"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchInReceiverExtensionMethodKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.InReceiverExtensionMethodKernel);

    private static readonly DifferentialCase s_refExt = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.RefReceiverExtensionMethodKernel),
        ["ref_ext_mutated_val", "ref_ext_mutated_tag"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchRefReceiverExtensionMethodKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.RefReceiverExtensionMethodKernel);

    private static readonly DifferentialCase s_staticDelegate = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.StaticMethodGroupConversionKernel),
        ["static_delegate_ret"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchStaticMethodGroupConversionKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.StaticMethodGroupConversionKernel);

    private static readonly DifferentialCase s_reducedExt = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.ReducedExtensionMethodGroupKernel),
        ["reduced_ext_delegate_captured_receiver"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchReducedExtensionMethodGroupKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.ReducedExtensionMethodGroupKernel);

    private static readonly DifferentialCase s_roPropertyAccess = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.ReadonlyPropertyAccessOnInAndFieldKernel),
        ["caller_tag_before_in_mut", "in_ext_defensive_copy_after", "ro_field_defensive_copy_after"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchReadonlyPropertyAccessOnInAndFieldKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.ReadonlyPropertyAccessOnInAndFieldKernel);

    // 3. Deconstruction, Records & Patterns
    private static readonly DifferentialCase s_tupleDeconstruct = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.TupleDeconstructionSwapAndRotationKernel),
        ["swap_a", "swap_b", "rot_x", "rot_y", "rot_z"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchTupleDeconstructionSwapAndRotationKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.TupleDeconstructionSwapAndRotationKernel);

    private static readonly DifferentialCase s_deconstructTargetOrder = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.DeconstructionSideEffectingTargetOrderKernel),
        ["arr_target_order"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionSideEffectingTargetOrderKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.DeconstructionSideEffectingTargetOrderKernel);

    private static readonly DifferentialCase s_deconstructDupTarget = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.DeconstructionDuplicateTargetOverwriteKernel),
        ["dup_target_last_wins"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionDuplicateTargetOverwriteKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.DeconstructionDuplicateTargetOverwriteKernel);

    private static readonly DifferentialCase s_deconstructWidening = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.DeconstructionImplicitWideningKernel),
        ["widened_deconstruct_sum"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionImplicitWideningKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.DeconstructionImplicitWideningKernel);

    private static readonly DifferentialCase s_recordWith = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.ReadonlyRecordStructWithExpressionKernel),
        ["ro_record_with_combined", "ro_record_with_side_effect_once"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchReadonlyRecordStructWithExpressionKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.ReadonlyRecordStructWithExpressionKernel);

    private static readonly DifferentialCase s_patternMatching = CreateCase(
        nameof(LanguageGenericsAndTypesDifferentialKernels.PatternMatchingAndSwitchExpressionKernel),
        ["null_and_not_null_pattern_flags", "switch_expr_val_and_single_eval"],
        static (xs, ys, r, gx, bx) => LanguageGenericsAndTypesDifferentialKernels.LaunchPatternMatchingAndSwitchExpressionKernel(xs, ys, r, gridDimX: gx, blockDimX: bx),
        LanguageGenericsAndTypesDifferentialKernels.PatternMatchingAndSwitchExpressionKernel);

    // 1. Generic Structs & Nested Types
    [SkippableFact]
    public void Test_GenericStructScopeUsing_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_genScopeUsing);

    [SkippableFact]
    public void Test_GenericStructPrimitivesAndNesting_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_genPrimitivesAndNesting);

    [SkippableFact]
    public void Test_ReadonlyStructWeightedSum_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_roWeightedSum);

    [SkippableFact]
    public void Test_NestedTypesInNamespaceAndClass_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_nestedTypes);

    // 2. Extension Methods & Delegates
    [SkippableFact]
    public void Test_ValueReceiverExtensionMethod_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_valExt);

    [SkippableFact]
    public void Test_InReceiverExtensionMethod_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_inExt);

    [SkippableFact]
    public void Test_RefReceiverExtensionMethod_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_refExt);

    [SkippableFact]
    public void Test_StaticMethodGroupConversion_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_staticDelegate);

    [SkippableFact]
    public void Test_ReducedExtensionMethodGroup_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_reducedExt);

    [SkippableFact]
    public void Test_ReadonlyPropertyAccessOnInAndField_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_roPropertyAccess);

    // 3. Deconstruction, Records & Patterns
    [SkippableFact]
    public void Test_TupleDeconstructionSwapAndRotation_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_tupleDeconstruct);

    [SkippableFact]
    public void Test_DeconstructionSideEffectingTargetOrder_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_deconstructTargetOrder);

    [SkippableFact]
    public void Test_DeconstructionDuplicateTargetOverwrite_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_deconstructDupTarget);

    [SkippableFact]
    public void Test_DeconstructionImplicitWidening_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_deconstructWidening);

    [SkippableFact]
    public void Test_ReadonlyRecordStructWithExpression_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_recordWith);

    [SkippableFact]
    public void Test_PatternMatchingAndSwitchExpression_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_patternMatching);
}
