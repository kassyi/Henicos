#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A divergence that is filed as an issue and not fixed yet: the cells of <paramref name="Column"/> in
/// <paramref name="Case"/> differ from the oracle on <paramref name="Paths"/>. A listed column may differ there without
/// failing the test, and it must still differ on every listed path that the process runs: once it matches for every seed,
/// the test fails and asks for the entry to be removed (XFAIL in LLVM lit). It is not a skip: the rest of the case is
/// still compared, and a fix cannot go unnoticed.
/// </summary>
/// <param name="Case">The <see cref="DifferentialCase.Name"/>, e.g. <c>IntegerMatrixKernels.Int32ArithmeticMatrixKernel</c>.</param>
/// <param name="Column">The column name (<see cref="DiffBuffer.ColumnNameOf"/>), e.g. <c>a&gt;&gt;&gt;b</c>.</param>
/// <param name="Paths">The paths on which the column differs; one or more flags.</param>
/// <param name="Issue">The issue that tracks the divergence.</param>
/// <param name="Hosts">The hosts on which the column differs; an entry for other hosts is ignored, also by the stale check.</param>
public sealed record KnownDivergence(string Case, string Column, DiffPath Paths, int Issue, DiffHost Hosts = DiffHost.Any)
{
    /// <summary>Gets whether the entry applies to the host this process runs on.</summary>
    public bool AppliesToThisHost => (Hosts & (OperatingSystem.IsWindows() ? DiffHost.Windows : DiffHost.Unix)) != 0;

    /// <summary>Returns whether the entry covers a cell of <paramref name="column"/> in <paramref name="caseName"/> on <paramref name="path"/>.</summary>
    /// <param name="caseName">The case.</param>
    /// <param name="column">The column name.</param>
    /// <param name="path">The path.</param>
    public bool Covers(string caseName, string column, DiffPath path) =>
        Case == caseName && Column == column && (Paths & path) != 0;

    /// <inheritdoc/>
    public override string ToString() => $"\"{Column}\" on {Paths.ToString().ToLowerInvariant()} (#{Issue})";
}

/// <summary>
/// The registry of <see cref="KnownDivergence"/> entries that <see cref="DifferentialRunner.Run"/> applies to every case
/// (design §5, D2). A matrix test that finds a new divergence files an issue (M1 or M2) and adds a line here; the PR that
/// fixes it removes the line, since the entry then fails.
/// </summary>
public static class KnownDivergences
{
    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),

        // F1, F2: Math.Sqrt(float) / Math.Floor|Ceiling|Round(float) return double in C# (#828); on native only MSVC picks the float overload
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Sqrt(f)", DiffPath.Cuda, Issue: 828),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Sqrt(f)", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Sqrt(f)*3.0", DiffPath.Cuda, Issue: 828),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Sqrt(f)*3.0", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Floor(f)/3", DiffPath.Cuda, Issue: 828),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Floor(f)/3", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Ceiling(f)/3", DiffPath.Cuda, Issue: 828),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Ceiling(f)/3", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Round(f)/3", DiffPath.Cuda, Issue: 828),
        new("M1DiffMathKernels.MathOfSingleMatrixKernel", "Math.Round(f)/3", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),

        // F3: foreach explicit element conversion from float/double to integer/enum (#995)
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(int in float)", DiffPath.Native, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(uint in float)", DiffPath.Native, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(long in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(ulong in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(short in float)", DiffPath.Native, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(ushort in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(byte in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(sbyte in float)", DiffPath.Native, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(char in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(IntEnum in float)", DiffPath.Native, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(ByteEnum in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(int in double)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(long in double)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(byte in double)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffConversionKernels.ForeachFloatToIntMatrixKernel", "foreach(int in yield float)", DiffPath.Native, Issue: 995),

        // F4: narrow enum ~ / +/- and signed enum wraparound (#995); GCC and Clang fold the signed overflow on native, MSVC wraps
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)~byteEnum", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)~ushortEnum", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "~ByteEnum.Zero>ByteEnum.Zero", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(byteEnum+b)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(byteEnum-b)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(byteEnum-byteEnum2)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(ushortEnum+us)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(ushortEnum-ushortEnum2)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(shortEnum+s)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "(int)(sbyteEnum-sb)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "intEnum+1<intEnum", DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "intEnum+1<intEnum", DiffPath.Native, Issue: 995, Hosts: DiffHost.Unix),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "intEnum-1>intEnum", DiffPath.Cuda, Issue: 995),
        new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel", "intEnum-1>intEnum", DiffPath.Native, Issue: 995, Hosts: DiffHost.Unix),

        // F5: compound assignment / increment receiver evaluation order & tuple deconstruction LHS order (#996)
        new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel", "(i==0?s:alt)[++i]+=v s[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel", "(i==0?s:alt)[++i]+=v alt[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel", "(i==0?s:alt)[++i]++ s[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel", "(i==0?s:alt)[++i]++ alt[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel", "(i,s[i])=(1,v) [0]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel", "(i,s[i])=(1,v) [1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),

        // F6: StackLinq fused Average(float) return type (#997)
        new("M1DiffStackLinqKernels.SingleFusedAverageKernel", "FusedAvgAsFloat", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.SingleFusedAverageKernel", "FusedAvgAsDouble", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.SingleFusedAverageKernel", "FusedAvgPlus0.1", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.SingleFusedAverageKernel", "WhereFusedAvg", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // F7: StackLinq unfused Average(float) return type (#997)
        new("M1DiffStackLinqKernels.SingleUnfusedAverageKernel", "UnfusedAvgAsFloat", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.SingleUnfusedAverageKernel", "UnfusedAvgAsDouble", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.SingleUnfusedAverageKernel", "UnfusedAvgPlus0.1", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // F8: StackLinq Contains(NaN) (#997)
        new("M1DiffStackLinqKernels.ContainsNaNKernel", "FusedContainsFloatNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.ContainsNaNKernel", "UnfusedContainsFloatNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.ContainsNaNKernel", "FusedContainsDoubleNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.ContainsNaNKernel", "UnfusedContainsDoubleNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // F9: StackLinq fused Contains argument evaluation timing (#997)
        new("M1DiffStackLinqKernels.ContainsEvaluationOrderKernel", "FusedContainsSideEffectCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.ContainsEvaluationOrderKernel", "FusedEmptyContainsSideEffectCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.ContainsEvaluationOrderKernel", "FusedSelectMutatesContainsArg", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // F10: StackLinq unfused Select + Skip / Where / Max (#997)
        new("M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel", "SelectSkipElementAtVal", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel", "SelectSkipElementAtCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel", "SelectWhereElementAtVal", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel", "SelectWhereElementAtCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new("M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel", "UnfusedSelectMaxCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // F11: empty stackalloc Span<T> / collection expression == null (#998)
        new("M1DiffSwitchKernels.EmptyStackSpanNullComparisonReproKernel", "stackalloc_zero_eq_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.EmptyStackSpanNullComparisonReproKernel", "stackalloc_zero_ne_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.EmptyStackSpanNullComparisonReproKernel", "collection_empty_eq_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.EmptyStackSpanNullComparisonReproKernel", "combined_null_flags", DiffPath.Native | DiffPath.Cuda, Issue: 998),

        // F12: Nullable<Memory<T>> / Nullable<ArraySegment<T>> null pattern (#998)
        new("M1DiffSwitchKernels.NullableArrayLikeDefaultValueNullCheckReproKernel", "mem_is_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.NullableArrayLikeDefaultValueNullCheckReproKernel", "mem_is_not_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.NullableArrayLikeDefaultValueNullCheckReproKernel", "mem_eq_ne_hasvalue", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.NullableArrayLikeDefaultValueNullCheckReproKernel", "seg_is_null_hasvalue", DiffPath.Native | DiffPath.Cuda, Issue: 998),

        // F13: Nullable<T> ?? conversion & narrow Nullable<T> ?? side effect (#998)
        new("M1DiffNullableKernels.CoalesceWiderNullableWithNarrowerNullable", "int_coalesce_short_val", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffNullableKernels.CoalesceWiderNullableWithNarrowerNullable", "int_coalesce_ushort_val", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.NarrowNullableCoalesceVarPromotionReproKernel", "byte_nullable_coalesce_inc", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.NarrowNullableCoalesceVarPromotionReproKernel", "ushort_nullable_coalesce_inc", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new("M1DiffSwitchKernels.NarrowNullableCoalesceVarPromotionReproKernel", "byte_nullable_ushort_coalesce_inc", DiffPath.Native | DiffPath.Cuda, Issue: 998),

        // F14: struct auto-property passed to in parameter without defensive copy (#999)
        new("M1DiffStructKernels.AutoPropertyPassedToInParameterDefensiveCopy", "in_snapshot_after_holder_mutated", DiffPath.Native | DiffPath.Cuda, Issue: 999),

        // F15: struct user-defined implicit operator ignored (#963)
        new("M1DiffStructKernels.StructUserDefinedImplicitConversion", "scaled_field_val", DiffPath.Native | DiffPath.Cuda, Issue: 963),
        new("M1DiffStructKernels.StructUserDefinedImplicitConversion", "converted_back_int", DiffPath.Native | DiffPath.Cuda, Issue: 963),

        // F16: BitOperations.PopCount/LeadingZeroCount/TrailingZeroCount/Log2 unsigned return in CPU Native C++ (#876)
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "PopCount(u)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "LeadingZeroCount(u)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "TrailingZeroCount(u)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "Log2(u)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "PopCount(ul)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "LeadingZeroCount(ul)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "TrailingZeroCount(ul)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "Log2(ul)<-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "PopCount(u)/-1", DiffPath.Native, Issue: 876),
        new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel", "LeadingZeroCount(ul)/-1", DiffPath.Native, Issue: 876),

        // F17: char scalar kernel argument truncated to 1 byte in CPU Native Launch (#971)
        new("M1DiffConversionKernels.CharScalarArgKernel", "(int)cArg", DiffPath.Native, Issue: 971),
        new("M1DiffConversionKernels.CharScalarArgKernel", "cArg==0x1234", DiffPath.Native, Issue: 971),

        // F18: ref ternary targeting scalar kernel parameter across barrier (#1000)
        new("M1DiffSwitchKernels.RefTernaryParameterWriteAcrossBarrierReproKernel", "ref_ternary_param_assign", DiffPath.Managed | DiffPath.Native, Issue: 1000),
        new("M1DiffSwitchKernels.RefTernaryParameterWriteAcrossBarrierReproKernel", "ref_ternary_param_compound", DiffPath.Managed | DiffPath.Native, Issue: 1000),
        new("M1DiffSwitchKernels.RefTernaryParameterWriteAcrossBarrierReproKernel", "ref_ternary_param_inc", DiffPath.Managed | DiffPath.Native, Issue: 1000),






    ];
}
