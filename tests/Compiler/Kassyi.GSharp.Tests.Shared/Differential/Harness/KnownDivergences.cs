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
    // The cases of the entries below, named through nameof so that a renamed kernel cannot leave an entry behind
    private const string MathOfSingle =
        $"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MathOfSingleMatrixKernel)}";
    private const string ForeachFloatToInt =
        $"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.ForeachFloatToIntMatrixKernel)}";
    private const string NarrowEnumAndSignedEnum =
        $"{nameof(NarrowIntegerDifferentialKernels)}.{nameof(NarrowIntegerDifferentialKernels.NarrowEnumAndSignedEnumMatrixKernel)}";
    private const string TargetEvaluationOrder =
        $"{nameof(NarrowIntegerDifferentialKernels)}.{nameof(NarrowIntegerDifferentialKernels.TargetEvaluationOrderMatrixKernel)}";
    private const string SingleFusedAverage =
        $"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.SingleFusedAverageKernel)}";
    private const string SingleUnfusedAverage =
        $"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.SingleUnfusedAverageKernel)}";
    private const string ContainsNaN =
        $"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.ContainsNaNKernel)}";
    private const string ContainsEvaluationOrder =
        $"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.ContainsEvaluationOrderKernel)}";
    private const string UnfusedSelectEvaluation =
        $"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.UnfusedSelectEvaluationKernel)}";
    private const string EmptyStackSpanNullComparison =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.EmptyStackSpanNullComparisonReproKernel)}";
    private const string NullableArrayLikeDefaultValueNullCheck =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableArrayLikeDefaultValueNullCheckReproKernel)}";
    private const string CoalesceWiderNullableWithNarrowerNullable =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.CoalesceWiderNullableWithNarrowerNullable)}";
    private const string NarrowNullableCoalesceVarPromotion =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NarrowNullableCoalesceVarPromotionReproKernel)}";
    private const string AutoPropertyPassedToInParameterDefensiveCopy =
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.AutoPropertyPassedToInParameterDefensiveCopy)}";
    private const string StructUserDefinedImplicitConversion =
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.StructUserDefinedImplicitConversion)}";
    private const string BitOpsReturnSign =
        $"{nameof(NarrowIntegerDifferentialKernels)}.{nameof(NarrowIntegerDifferentialKernels.BitOpsReturnSignMatrixKernel)}";
    private const string CharScalarArg =
        $"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.CharScalarArgKernel)}";
    private const string RefTernaryParameterWriteAcrossBarrier =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.RefTernaryParameterWriteAcrossBarrierReproKernel)}";
    private const string SubWarpConditionalShuffle =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.SubWarpConditionalShuffleKernel)}";
    private const string GenericStructStaticSharedMemory =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.GenericStructStaticSharedMemoryKernel)}";

    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),

        // Math.Sqrt(float) / Math.Floor|Ceiling|Round(float) return double in C# (#828); on native only MSVC picks the float overload
        new(MathOfSingle, "Math.Sqrt(f)", DiffPath.Cuda, Issue: 828),
        new(MathOfSingle, "Math.Sqrt(f)", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new(MathOfSingle, "Math.Sqrt(f)*3.0", DiffPath.Cuda, Issue: 828),
        new(MathOfSingle, "Math.Sqrt(f)*3.0", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new(MathOfSingle, "Math.Floor(f)/3", DiffPath.Cuda, Issue: 828),
        new(MathOfSingle, "Math.Floor(f)/3", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new(MathOfSingle, "Math.Ceiling(f)/3", DiffPath.Cuda, Issue: 828),
        new(MathOfSingle, "Math.Ceiling(f)/3", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),
        new(MathOfSingle, "Math.Round(f)/3", DiffPath.Cuda, Issue: 828),
        new(MathOfSingle, "Math.Round(f)/3", DiffPath.Native, Issue: 828, Hosts: DiffHost.Windows),

        // foreach explicit element conversion from float/double to integer/enum (#995)
        new(ForeachFloatToInt, "foreach(int in float)", DiffPath.Native, Issue: 995),
        new(ForeachFloatToInt, "foreach(uint in float)", DiffPath.Native, Issue: 995),
        new(ForeachFloatToInt, "foreach(long in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(ulong in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(short in float)", DiffPath.Native, Issue: 995),
        new(ForeachFloatToInt, "foreach(ushort in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(byte in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(sbyte in float)", DiffPath.Native, Issue: 995),
        new(ForeachFloatToInt, "foreach(char in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(IntEnum in float)", DiffPath.Native, Issue: 995),
        new(ForeachFloatToInt, "foreach(ByteEnum in float)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(int in double)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(long in double)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(byte in double)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(ForeachFloatToInt, "foreach(int in yield float)", DiffPath.Native, Issue: 995),

        // narrow enum ~ / +/- and signed enum wraparound (#995); GCC and Clang fold the signed overflow on native, MSVC wraps
        new(NarrowEnumAndSignedEnum, "(int)~byteEnum", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)~ushortEnum", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "~ByteEnum.Zero>ByteEnum.Zero", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(byteEnum+b)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(byteEnum-b)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(byteEnum-byteEnum2)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(ushortEnum+us)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(ushortEnum-ushortEnum2)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(shortEnum+s)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "(int)(sbyteEnum-sb)", DiffPath.Native | DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "intEnum+1<intEnum", DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "intEnum+1<intEnum", DiffPath.Native, Issue: 995, Hosts: DiffHost.Unix),
        new(NarrowEnumAndSignedEnum, "intEnum-1>intEnum", DiffPath.Cuda, Issue: 995),
        new(NarrowEnumAndSignedEnum, "intEnum-1>intEnum", DiffPath.Native, Issue: 995, Hosts: DiffHost.Unix),

        // compound assignment / increment receiver evaluation order & tuple deconstruction LHS order (#996)
        new(TargetEvaluationOrder, "(i==0?s:alt)[++i]+=v s[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new(TargetEvaluationOrder, "(i==0?s:alt)[++i]+=v alt[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new(TargetEvaluationOrder, "(i==0?s:alt)[++i]++ s[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new(TargetEvaluationOrder, "(i==0?s:alt)[++i]++ alt[1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new(TargetEvaluationOrder, "(i,s[i])=(1,v) [0]", DiffPath.Native | DiffPath.Cuda, Issue: 996),
        new(TargetEvaluationOrder, "(i,s[i])=(1,v) [1]", DiffPath.Native | DiffPath.Cuda, Issue: 996),

        // StackLinq fused Average(float) return type (#997)
        new(SingleFusedAverage, "FusedAvgAsFloat", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(SingleFusedAverage, "FusedAvgAsDouble", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(SingleFusedAverage, "FusedAvgPlus0.1", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(SingleFusedAverage, "WhereFusedAvg", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // StackLinq unfused Average(float) return type (#997)
        new(SingleUnfusedAverage, "UnfusedAvgAsFloat", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(SingleUnfusedAverage, "UnfusedAvgAsDouble", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(SingleUnfusedAverage, "UnfusedAvgPlus0.1", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // StackLinq Contains(NaN) (#997)
        new(ContainsNaN, "FusedContainsFloatNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(ContainsNaN, "UnfusedContainsFloatNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(ContainsNaN, "FusedContainsDoubleNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(ContainsNaN, "UnfusedContainsDoubleNaN", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // StackLinq fused Contains argument evaluation timing (#997)
        new(ContainsEvaluationOrder, "FusedContainsSideEffectCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(ContainsEvaluationOrder, "FusedEmptyContainsSideEffectCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(ContainsEvaluationOrder, "FusedSelectMutatesContainsArg", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // StackLinq unfused Select + Skip / Where / Max (#997)
        new(UnfusedSelectEvaluation, "SelectSkipElementAtVal", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(UnfusedSelectEvaluation, "SelectSkipElementAtCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(UnfusedSelectEvaluation, "SelectWhereElementAtVal", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(UnfusedSelectEvaluation, "SelectWhereElementAtCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),
        new(UnfusedSelectEvaluation, "UnfusedSelectMaxCount", DiffPath.Native | DiffPath.Cuda, Issue: 997),

        // empty stackalloc Span<T> / collection expression == null (#998)
        new(EmptyStackSpanNullComparison, "stackalloc_zero_eq_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(EmptyStackSpanNullComparison, "stackalloc_zero_ne_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(EmptyStackSpanNullComparison, "collection_empty_eq_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(EmptyStackSpanNullComparison, "combined_null_flags", DiffPath.Native | DiffPath.Cuda, Issue: 998),

        // Nullable<Memory<T>> / Nullable<ArraySegment<T>> null pattern (#998)
        new(NullableArrayLikeDefaultValueNullCheck, "mem_is_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(NullableArrayLikeDefaultValueNullCheck, "mem_is_not_null", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(NullableArrayLikeDefaultValueNullCheck, "mem_eq_ne_hasvalue", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(NullableArrayLikeDefaultValueNullCheck, "seg_is_null_hasvalue", DiffPath.Native | DiffPath.Cuda, Issue: 998),

        // Nullable<T> ?? conversion & narrow Nullable<T> ?? side effect (#998)
        new(CoalesceWiderNullableWithNarrowerNullable, "int_coalesce_short_val", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(CoalesceWiderNullableWithNarrowerNullable, "int_coalesce_ushort_val", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(NarrowNullableCoalesceVarPromotion, "byte_nullable_coalesce_inc", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(NarrowNullableCoalesceVarPromotion, "ushort_nullable_coalesce_inc", DiffPath.Native | DiffPath.Cuda, Issue: 998),
        new(NarrowNullableCoalesceVarPromotion, "byte_nullable_ushort_coalesce_inc", DiffPath.Native | DiffPath.Cuda, Issue: 998),

        // struct auto-property passed to in parameter without defensive copy (#999)
        new(AutoPropertyPassedToInParameterDefensiveCopy, "in_snapshot_after_holder_mutated", DiffPath.Native | DiffPath.Cuda, Issue: 999),

        // struct user-defined implicit operator ignored (#963)
        new(StructUserDefinedImplicitConversion, "scaled_field_val", DiffPath.Native | DiffPath.Cuda, Issue: 963),
        new(StructUserDefinedImplicitConversion, "converted_back_int", DiffPath.Native | DiffPath.Cuda, Issue: 963),

        // BitOperations.PopCount/LeadingZeroCount/TrailingZeroCount/Log2 unsigned return in CPU Native C++ (#876)
        new(BitOpsReturnSign, "PopCount(u)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "LeadingZeroCount(u)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "TrailingZeroCount(u)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "Log2(u)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "PopCount(ul)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "LeadingZeroCount(ul)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "TrailingZeroCount(ul)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "Log2(ul)<-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "PopCount(u)/-1", DiffPath.Native, Issue: 876),
        new(BitOpsReturnSign, "LeadingZeroCount(ul)/-1", DiffPath.Native, Issue: 876),

        // char scalar kernel argument truncated to 1 byte in CPU Native Launch (#971)
        new(CharScalarArg, "(int)cArg", DiffPath.Native, Issue: 971),
        new(CharScalarArg, "cArg==0x1234", DiffPath.Native, Issue: 971),

        // ref ternary targeting scalar kernel parameter across barrier (#1000)
        new(RefTernaryParameterWriteAcrossBarrier, "ref_ternary_param_assign", DiffPath.Managed | DiffPath.Native, Issue: 1000),
        new(RefTernaryParameterWriteAcrossBarrier, "ref_ternary_param_compound", DiffPath.Managed | DiffPath.Native, Issue: 1000),
        new(RefTernaryParameterWriteAcrossBarrier, "ref_ternary_param_inc", DiffPath.Managed | DiffPath.Native, Issue: 1000),

        // Warp.ShuffleXor after sub-warp conditional ShuffleDown on CPU emulator (#1001)
        new(SubWarpConditionalShuffle, "reconvergedFullWarpXor16", DiffPath.Managed | DiffPath.Native, Issue: 1001),

        // generic struct static method SharedMemory.Allocate<T> collides across type arguments (#543)
        new(GenericStructStaticSharedMemory, "SharedSlot<int>", DiffPath.Managed | DiffPath.Native, Issue: 543),
    ];
}
