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

    private const string ArrayIndexBounds =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArrayIndexBoundsKernel)}";
    private const string SpanCanaryIndexBounds =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanCanaryIndexBoundsKernel)}";
    private const string SpanSliceStart =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanSliceStartKernel)}";
    private const string SpanSliceStartCount =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanSliceStartCountKernel)}";
    private const string ArraySegmentBounds =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArraySegmentBoundsKernel)}";
    private const string MemoryBounds =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.MemoryBoundsKernel)}";
    private const string InlineArrayBounds =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.InlineArrayBoundsKernel)}";
    private const string BoundsCheckEliminationOffByOne =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationOffByOneKernel)}";
    private const string BoundsCheckEliminationWrongSpan =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWrongSpanKernel)}";
    private const string BoundsCheckEliminationPostIncrement =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationPostIncrementKernel)}";
    private const string BoundsCheckEliminationUpperOnly =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationUpperOnlyKernel)}";
    private const string AllThreadsNullDereferenceTrap =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.AllThreadsNullDereferenceTrapKernel)}";
    private const string AllThreadsEmptyFirstTrap =
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.AllThreadsEmptyFirstTrapKernel)}";

    private const string Int32Divide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32DivideKernel)}";
    private const string Int32Remainder =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32RemainderKernel)}";
    private const string UInt32Divide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UInt32DivideKernel)}";
    private const string UInt32Remainder =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UInt32RemainderKernel)}";
    private const string Int64Divide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int64DivideKernel)}";
    private const string UInt64Divide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UInt64DivideKernel)}";
    private const string Int16CompoundDivide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int16CompoundDivideKernel)}";
    private const string SByteCompoundRemainder =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.SByteCompoundRemainderKernel)}";

    private const string Int32MinValueDivide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueDivideKernel)}";
    private const string Int32MinValueRemainder =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int32MinValueRemainderKernel)}";
    private const string Int64MinValueDivide =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueDivideKernel)}";
    private const string Int64MinValueRemainder =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.Int64MinValueRemainderKernel)}";

    private const string MathAbsInt32 =
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsInt32Kernel)}";

    private const string NullableInt32ValueAccess =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.NullableInt32ValueAccessKernel)}";
    private const string NullableStructValueAccess =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.NullableStructValueAccessKernel)}";
    private const string MathClampInt32 =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampInt32Kernel)}";
    private const string MathClampSingle =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampSingleKernel)}";
    private const string MathClampDouble =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampDoubleKernel)}";
    private const string ElementAtOrDefaultNegativeIndexSideEffect =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.ElementAtOrDefaultNegativeIndexSideEffectKernel)}";

    private const string StackLinqFirst =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqFirstKernel)}";
    private const string StackLinqSingle =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqSingleKernel)}";
    private const string StackLinqSingleTwoElements =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqSingleKernel)}_TwoElements";
    private const string StackLinqLast =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqLastKernel)}";
    private const string StackLinqMin =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqMinKernel)}";
    private const string StackLinqMax =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqMaxKernel)}";
    private const string StackLinqAverage =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqAverageKernel)}";
    private const string StackLinqAggregate =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqAggregateKernel)}";
    private const string StackLinqElementAtNegative =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqElementAtKernel)}_NegativeIndex";
    private const string StackLinqElementAtPastEnd =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqElementAtKernel)}_PastEnd";

    private const string MemoryExtensionsSortLengthMismatch =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MemoryExtensionsSortLengthMismatchKernel)}";
    private const string MemoryExtensionsSortComparerTrap =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MemoryExtensionsSortComparerTrapKernel)}";

    private const string TrapNonContinuationGuard =
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.TrapNonContinuationGuardKernel)}";

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

        // Span/Slice/ArraySegment/Memory/InlineArray bounds and BCE guards on native (#718)
        new(SpanCanaryIndexBounds, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(SpanSliceStart, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(SpanSliceStartCount, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(ArraySegmentBounds, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(MemoryBounds, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(InlineArrayBounds, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(BoundsCheckEliminationOffByOne, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(BoundsCheckEliminationWrongSpan, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(BoundsCheckEliminationPostIncrement, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        new(BoundsCheckEliminationUpperOnly, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),

        // Array and span bounds, BCE guards, simultaneous traps, integer divide-by-zero, and Math.Abs(MinValue) on CUDA (#729)
        new(ArrayIndexBounds, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(SpanCanaryIndexBounds, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(SpanSliceStart, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(SpanSliceStartCount, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(ArraySegmentBounds, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(MemoryBounds, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(InlineArrayBounds, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(BoundsCheckEliminationOffByOne, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(BoundsCheckEliminationWrongSpan, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(BoundsCheckEliminationPostIncrement, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(BoundsCheckEliminationUpperOnly, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(AllThreadsNullDereferenceTrap, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(AllThreadsEmptyFirstTrap, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(Int32Divide, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(Int32Remainder, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(UInt32Divide, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(UInt32Remainder, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(Int64Divide, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(UInt64Divide, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(Int16CompoundDivide, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(SByteCompoundRemainder, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(MathAbsInt32, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),

        // Nullable<T>.Value and Math.Clamp traps on native and CUDA (#718)
        new(NullableInt32ValueAccess, KnownDivergence.ExceptionColumn, DiffPath.Native | DiffPath.Cuda, Issue: 718),
        new(NullableStructValueAccess, KnownDivergence.ExceptionColumn, DiffPath.Native | DiffPath.Cuda, Issue: 718),
        new(MathClampInt32, KnownDivergence.ExceptionColumn, DiffPath.Native | DiffPath.Cuda, Issue: 718),
        new(MathClampSingle, KnownDivergence.ExceptionColumn, DiffPath.Native | DiffPath.Cuda, Issue: 718),
        new(MathClampDouble, KnownDivergence.ExceptionColumn, DiffPath.Native | DiffPath.Cuda, Issue: 718),

        // StackLinq empty sequence, bounds traps, Sort faults, and trap guard on CUDA (#729)
        new(StackLinqFirst, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqSingle, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqSingleTwoElements, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqLast, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqMin, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqMax, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqAverage, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqAggregate, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqElementAtNegative, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(StackLinqElementAtPastEnd, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(MemoryExtensionsSortLengthMismatch, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(MemoryExtensionsSortComparerTrap, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),
        new(TrapNonContinuationGuard, KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 729),

        // Unchecked MinValue / -1 and MinValue % -1 Managed path throws OverflowException (#718)
        new(Int32MinValueDivide, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        new(Int32MinValueRemainder, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        new(Int64MinValueDivide, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        new(Int64MinValueRemainder, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),

        // Math.Abs(MinValue) overflow trap (#947)
        new(MathAbsInt32, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 947),

        // StackLinq ElementAtOrDefault(-1) side effects (#1060)
        new(ElementAtOrDefaultNegativeIndexSideEffect, "select_neg_eval_count", DiffPath.Managed, Issue: 1060),
        new(ElementAtOrDefaultNegativeIndexSideEffect, "where_neg_eval_count", DiffPath.Managed | DiffPath.Native | DiffPath.Cuda, Issue: 1060),
    ];
}
