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
    private const string EmptyStackSpanNullComparison =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.EmptyStackSpanNullComparisonReproKernel)}";
    private const string NullableArrayLikeDefaultValueNullCheck =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableArrayLikeDefaultValueNullCheckReproKernel)}";
    private const string CoalesceWiderNullableWithNarrowerNullable =
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.CoalesceWiderNullableWithNarrowerNullable)}";
    private const string AutoPropertyPassedToInParameterDefensiveCopy =
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.AutoPropertyPassedToInParameterDefensiveCopy)}";
    private const string StructUserDefinedImplicitConversion =
        $"{nameof(StructDifferentialKernels)}.{nameof(StructDifferentialKernels.StructUserDefinedImplicitConversion)}";
    private const string SubWarpConditionalShuffle =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.SubWarpConditionalShuffleKernel)}";
    private const string GenericStructStaticSharedMemory =
        $"{nameof(BarrierAndWarpDifferentialKernels)}.{nameof(BarrierAndWarpDifferentialKernels.GenericStructStaticSharedMemoryKernel)}";
    private const string CharAllPositions =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.CharAllPositionsKernel)}";
    private const string NullableBlitScalarArg =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableBlitScalarArgKernel)}";
    private const string NullableConvertPositions =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableConvertPositionsKernel)}";
    private const string ExplicitUnionWithConvertArm =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.ExplicitUnionWithConvertArmKernel)}";
    private const string InlineArrayNullableVec2 =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.InlineArrayNullableVec2Kernel)}";
    private const string OverlappingSpanSlice =
        $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.OverlappingSpanSliceKernel)}";
    private const string AliasedRefScalars =
        $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.AliasedRefScalarsKernel)}";
    private const string CrossAliasInDeepArrayAndRwArray =
        $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.CrossAliasInDeepArrayAndRwArrayKernel)}";
    private const string PtrLengthGuardedScale =
        $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.PtrLengthGuardedScaleKernel)}";
    private const string InterlockedReturnExpression =
        $"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedReturnExpressionKernel)}";

    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),

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

        // struct auto-property passed to in parameter without defensive copy (#999)
        new(AutoPropertyPassedToInParameterDefensiveCopy, "in_snapshot_after_holder_mutated", DiffPath.Native | DiffPath.Cuda, Issue: 999),

        // struct user-defined implicit operator ignored (#963)
        new(StructUserDefinedImplicitConversion, "scaled_field_val", DiffPath.Native | DiffPath.Cuda, Issue: 963),
        new(StructUserDefinedImplicitConversion, "converted_back_int", DiffPath.Native | DiffPath.Cuda, Issue: 963),

        // Warp.ShuffleXor after sub-warp conditional ShuffleDown on CPU emulator (#1001)
        new(SubWarpConditionalShuffle, "reconvergedFullWarpXor16", DiffPath.Managed | DiffPath.Native, Issue: 1001),

        // generic struct static method SharedMemory.Allocate<T> collides across type arguments (#543)
        new(GenericStructStaticSharedMemory, "SharedSlot<int>", DiffPath.Managed | DiffPath.Native, Issue: 543),

        // char by-value / in kernel arguments truncated in CPU Native Launch (#971)
        new(CharAllPositions, "char_val", DiffPath.Native, Issue: 971),
        new(CharAllPositions, "char_in", DiffPath.Native, Issue: 971),
        new(CharAllPositions, "ov", DiffPath.Native, Issue: 971),
        new(CharAllPositions, "__result", DiffPath.Native, Issue: 971),

        // Nullable<T> scalar / byref kernel arguments throw MarshalDirectiveException in CPU Native Launch (#961)
        new(NullableBlitScalarArg, "v_has", DiffPath.Native, Issue: 961),
        new(NullableBlitScalarArg, "v_val", DiffPath.Native, Issue: 961),
        new(NullableBlitScalarArg, "rb_has", DiffPath.Native, Issue: 961),
        new(NullableBlitScalarArg, "rb_val", DiffPath.Native, Issue: 961),
        new(NullableBlitScalarArg, "rb", DiffPath.Native, Issue: 961),
        new(NullableBlitScalarArg, "oc", DiffPath.Native, Issue: 961),

        // Nullable<Vector2> host 12B vs CUDA 16B layout mismatch (#961)
        new(NullableConvertPositions, "v_x", DiffPath.Cuda, Issue: 961),
        new(NullableConvertPositions, "v_y", DiffPath.Cuda, Issue: 961),
        new(NullableConvertPositions, "rv_x", DiffPath.Cuda, Issue: 961),
        new(NullableConvertPositions, "rv_y", DiffPath.Cuda, Issue: 961),
        new(NullableConvertPositions, "h", DiffPath.Cuda, Issue: 961),

        // LayoutKind.Explicit union with Convert arm overwritten by subsequent union arm during CUDA unmarshal (#1006)
        new(ExplicitUnionWithConvertArm, "u", DiffPath.Cuda, Issue: 1006),

        // [InlineArray(2)] of Nullable<Vector2> block-copied without per-element conversion on CUDA (#961)
        new(InlineArrayNullableVec2, "item0_x", DiffPath.Cuda, Issue: 961),
        new(InlineArrayNullableVec2, "item0_y", DiffPath.Cuda, Issue: 961),
        new(InlineArrayNullableVec2, "item1_has", DiffPath.Cuda, Issue: 961),
        new(InlineArrayNullableVec2, "item1_x", DiffPath.Cuda, Issue: 961),
        new(InlineArrayNullableVec2, "item1_y", DiffPath.Cuda, Issue: 961),
        new(InlineArrayNullableVec2, "h", DiffPath.Cuda, Issue: 961),

        // Partially overlapping Span<T> slices from same array do not share device memory on CUDA (#857)
        new(OverlappingSpanSlice, "backing", DiffPath.Cuda, Issue: 857),
        new(OverlappingSpanSlice, "observed", DiffPath.Cuda, Issue: 857),

        // Aliased ref scalar parameters allocate separate device buffers and overwrite on DtoH copyback on CUDA
        new(AliasedRefScalars, "sharedRef", DiffPath.Cuda, Issue: 1061),
        new(AliasedRefScalars, "observed", DiffPath.Cuda, Issue: 1061),

        // [In] DeepStruct[] element array field cloned unconditionally on CPU even when aliasing writable int[] (#985)
        new(CrossAliasInDeepArrayAndRwArray, "observed", DiffPath.Managed | DiffPath.Native, Issue: 985),

        // Launch{Kernel}Ptr with raw IntPtr passes Length = 0 in GSharpArrayProxy (#720)
        new(PtrLengthGuardedScale, "data", DiffPath.Cuda, Issue: 720),
        new(PtrLengthGuardedScale, "outLen", DiffPath.Cuda, Issue: 720),

        // Interlocked long overloads return unsigned long long in C++, signed 32-bit add overflow UB in CUDA prelude, and named arguments are not reordered (#1057, #1058, #1059)
        new(InterlockedReturnExpression, "add64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.Add(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "exch64_sar_sign", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.Exchange(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "cas64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.CompareExchange(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "add32_wrap_min", DiffPath.Cuda, Issue: 1058), // __gsharp_atomicAdd(int*, int) performs signed int addition without unsigned cast, triggering C++ signed overflow UB under nvcc (#1058)
        new(InterlockedReturnExpression, "cas32_named_args_reversed", DiffPath.Native | DiffPath.Cuda, Issue: 1059), // BclIntrinsicTranspilerRule does not reorder named arguments (#1059)
    ];
}
