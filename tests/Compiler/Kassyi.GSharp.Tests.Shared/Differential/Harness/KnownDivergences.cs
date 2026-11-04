#nullable enable
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The registry of <see cref="KnownDivergence"/> entries that <see cref="DifferentialRunner.Run"/> applies to every case.
/// A matrix test that finds a new divergence files an issue (M1 or M2) and adds a line here; the PR that fixes it removes the line, since the entry then fails.
/// </summary>
public static class KnownDivergences
{
    // The cases of the entries below, named through nameof so that a renamed kernel cannot leave an entry behind
    private const string HelperDynamicSharedMemoryZeroBytes =
        $"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.HelperDynamicSharedMemoryZeroBytesKernel)}";
    private const string HelperDynamicSharedMemoryGrowthAfterLaterDecl =
        $"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.HelperDynamicSharedMemoryGrowthAfterLaterDeclKernel)}";
    private const string HelperDynamicSharedMemoryNegativeCount =
        $"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.HelperDynamicSharedMemoryNegativeCountKernel)}";
    private const string GridStrideLoopUniformWarpShuffle =
        $"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.GridStrideLoopUniformWarpShuffleKernel)}";

    private const string NullableBlitScalarArg =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableBlitScalarArgKernel)}";
    private const string NullableConvertPositions =
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableConvertPositionsKernel)}";
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

    /// <summary>Gets every registered entry.</summary>
    public static IReadOnlyList<KnownDivergence> All { get; } =
    [
        // new("IntegerMatrixKernels.Int32ArithmeticMatrixKernel", "a>>>b", DiffPath.Native | DiffPath.Cuda, Issue: 722),

        // dynamic SharedMemory<T>(count) inside non-suspending [GpuDeviceFunction] is not rewritten on Managed (#904)
        new(HelperDynamicSharedMemoryZeroBytes, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 904),
        new(HelperDynamicSharedMemoryGrowthAfterLaterDecl, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 904),
        new(HelperDynamicSharedMemoryNegativeCount, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 904),

        // Warp.ShuffleDownSync directly inside Gpu.GridStrideLoop lambda is not split by StateMachineLowerer (#1091)
        new(GridStrideLoopUniformWarpShuffle, "iter0_down1", DiffPath.Native, Issue: 1091),
        new(GridStrideLoopUniformWarpShuffle, "iter1_down1", DiffPath.Native, Issue: 1091),

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

        // Span/Slice/ArraySegment/Memory/InlineArray bounds and BCE guards on native (#718)
        new(SpanCanaryIndexBounds, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
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

        // Unchecked MinValue / -1 and MinValue % -1 Managed path throws OverflowException (#718)
        new(Int32MinValueDivide, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        new(Int32MinValueRemainder, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        new(Int64MinValueDivide, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        new(Int64MinValueRemainder, KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),

        // Math.Abs(MinValue) overflow trap (#947)
        new(MathAbsInt32, KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 947),

        // StackLinq ElementAtOrDefault(-1) side effects (#1060)
        new(ElementAtOrDefaultNegativeIndexSideEffect, "select_neg_eval_count", DiffPath.Managed | DiffPath.Native | DiffPath.Cuda, Issue: 1060),
        new(ElementAtOrDefaultNegativeIndexSideEffect, "where_neg_eval_count", DiffPath.Managed | DiffPath.Native | DiffPath.Cuda, Issue: 1060),

        // Interlocked long overloads return unsigned long long in C++, and signed 32-bit add overflow UB in CUDA prelude (#1057, #1058)
        new(InterlockedReturnExpression, "add64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.Add(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "exch64_sar_sign", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.Exchange(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "cas64_lt_zero", DiffPath.Native | DiffPath.Cuda, Issue: 1057), // Interlocked.CompareExchange(ref long) returns unsigned long long in C++ (#1057)
        new(InterlockedReturnExpression, "add32_wrap_min", DiffPath.Cuda, Issue: 1058), // __gsharp_atomicAdd(int*, int) performs signed int addition without unsigned cast, triggering C++ signed overflow UB under nvcc (#1058)
    ];
}
