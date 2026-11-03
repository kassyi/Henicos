#nullable enable
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The registry of <see cref="KnownDivergence"/> entries that <see cref="DifferentialRunner.Run"/> applies to every case
/// (design §5, D2). A matrix test that finds a new divergence files an issue (M1 or M2) and adds a line here; the PR that
/// fixes it removes the line, since the entry then fails.
/// </summary>
public static class KnownDivergences
{
    // The cases of the entries below, named through nameof so that a renamed kernel cannot leave an entry behind
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
    private const string CharScalarArg =
        $"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.CharScalarArgKernel)}";
    private const string RefTernaryParameterWriteAcrossBarrier =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.RefTernaryParameterWriteAcrossBarrierReproKernel)}";
    private const string SubWarpConditionalShuffle =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.SubWarpConditionalShuffleKernel)}";
    private const string GenericStructStaticSharedMemory =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.GenericStructStaticSharedMemoryKernel)}";
    private const string InterlockedReturnExpression =
        $"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedReturnExpressionKernel)}";

    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),

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

        // Interlocked long overloads return unsigned long long in C++, signed 32-bit add overflow UB in CUDA prelude, and named arguments are not reordered (S23, S24, S46)
        new(InterlockedReturnExpression, "add64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // S23: Interlocked.Add(ref long) returns unsigned long long in C++
        new(InterlockedReturnExpression, "exch64_sar_sign", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // S23: Interlocked.Exchange(ref long) returns unsigned long long in C++
        new(InterlockedReturnExpression, "cas64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // S23: Interlocked.CompareExchange(ref long) returns unsigned long long in C++
        new(InterlockedReturnExpression, "add32_wrap_min", DiffPath.Cuda, Issue: 1058), // S24: __gsharp_atomicAdd(int*, int) performs signed int addition without unsigned cast, triggering C++ signed overflow UB under nvcc
        new(InterlockedReturnExpression, "cas32_named_args_reversed", DiffPath.Native | DiffPath.Cuda, Issue: 1059), // S46: BclIntrinsicTranspilerRule does not reorder named arguments
    ];
}
