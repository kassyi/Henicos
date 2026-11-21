#nullable enable
using System.Runtime.CompilerServices;
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
        Int32TripletLaunch launch,
        string[] columns,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) =>
            {
                var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers()
                    .In("xs", xs)
                    .In("ys", ys)
                    .Out<int>("r", rows, columns, OutputPolicy.Exact);
            },
            shape: LaunchShape.ForRows(Rows, blockX: 64),
            firstInputName: "xs",
            secondInputName: "ys",
            outputName: "r",
            launchExpression: launchExpression);

    // 1. Generic Structs & Nested Types
    internal static readonly DifferentialCase s_genScopeUsing = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchGenericStructScopeUsingKernel,
        ["int_box_val", "int_box_tag_plus_ten", "int_box_disposed_marker"]);

    internal static readonly DifferentialCase s_genPrimitivesAndNesting = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchGenericStructPrimitivesAndNestingKernel,
        ["uint_box_val", "short_box_val", "nested_pair_first_val", "nested_pair_first_tag", "nested_pair_second"]);

    internal static readonly DifferentialCase s_roWeightedSum = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchReadonlyStructWeightedSumKernel,
        ["ro_cell_weighted_sum"]);

    internal static readonly DifferentialCase s_nestedTypes = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchNestedTypesInNamespaceAndClassKernel,
        ["nested_scope_add", "nested_scope_xor", "nested_scope_scale"]);

    // 2. Extension Methods & Delegates
    internal static readonly DifferentialCase s_valExt = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchValueReceiverExtensionMethodKernel,
        ["val_ext_ret", "val_ext_caller_unchanged"]);

    internal static readonly DifferentialCase s_inExt = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchInReceiverExtensionMethodKernel,
        ["in_ext_ret"]);

    internal static readonly DifferentialCase s_refExt = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchRefReceiverExtensionMethodKernel,
        ["ref_ext_mutated_val", "ref_ext_mutated_tag"]);

    internal static readonly DifferentialCase s_staticDelegate = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchStaticMethodGroupConversionKernel,
        ["static_delegate_ret"]);

    internal static readonly DifferentialCase s_reducedExt = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchReducedExtensionMethodGroupKernel,
        ["reduced_ext_delegate_captured_receiver"]);

    internal static readonly DifferentialCase s_roPropertyAccess = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchReadonlyPropertyAccessOnInAndFieldKernel,
        ["caller_tag_before_in_mut", "in_ext_defensive_copy_after", "ro_field_defensive_copy_after"]);

    // 3. Deconstruction, Records & Patterns
    internal static readonly DifferentialCase s_tupleDeconstruct = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchTupleDeconstructionSwapAndRotationKernel,
        ["swap_a", "swap_b", "rot_x", "rot_y", "rot_z"]);

    internal static readonly DifferentialCase s_deconstructTargetOrder = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionSideEffectingTargetOrderKernel,
        ["arr_target_order"]);

    internal static readonly DifferentialCase s_deconstructDupTarget = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionDuplicateTargetOverwriteKernel,
        ["dup_target_last_wins"]);

    internal static readonly DifferentialCase s_deconstructWidening = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionImplicitWideningKernel,
        ["widened_deconstruct_sum"]);

    internal static readonly DifferentialCase s_recordWith = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchReadonlyRecordStructWithExpressionKernel,
        ["ro_record_with_combined", "ro_record_with_side_effect_once"]);

    internal static readonly DifferentialCase s_patternMatching = CreateCase(
        LanguageGenericsAndTypesDifferentialKernels.LaunchPatternMatchingAndSwitchExpressionKernel,
        ["null_and_not_null_pattern_flags", "switch_expr_val_and_single_eval"]);

    // 1. Generic Structs & Nested Types
    [SkippableFact]
    public void GenericStruct_ScopeUsing_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_genScopeUsing);

    [SkippableFact]
    public void GenericStruct_PrimitivesAndNesting_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_genPrimitivesAndNesting);

    [SkippableFact]
    public void ReadonlyStruct_WeightedSum_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_roWeightedSum);

    [SkippableFact]
    public void NestedTypes_InNamespaceAndClass_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_nestedTypes);

    // 2. Extension Methods & Delegates
    [SkippableFact]
    public void ExtensionMethod_ValueReceiver_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_valExt);

    [SkippableFact]
    public void ExtensionMethod_InReceiver_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_inExt);

    [SkippableFact]
    public void ExtensionMethod_RefReceiver_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_refExt);

    [SkippableFact]
    public void MethodGroup_StaticConversion_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_staticDelegate);

    [SkippableFact]
    public void MethodGroup_ReducedExtension_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_reducedExt);

    [SkippableFact]
    public void ReadonlyProperty_AccessOnInAndField_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_roPropertyAccess);

    // 3. Deconstruction, Records & Patterns
    [SkippableFact]
    public void TupleDeconstruction_SwapAndRotation_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_tupleDeconstruct);

    [SkippableFact]
    public void Deconstruction_SideEffectingTargetOrder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deconstructTargetOrder);

    [SkippableFact]
    public void Deconstruction_DuplicateTargetOverwrite_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deconstructDupTarget);

    [SkippableFact]
    public void Deconstruction_ImplicitWidening_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deconstructWidening);

    [SkippableFact]
    public void ReadonlyRecordStruct_WithExpression_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_recordWith);

    [SkippableFact]
    public void PatternMatching_SwitchExpression_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_patternMatching);
}
