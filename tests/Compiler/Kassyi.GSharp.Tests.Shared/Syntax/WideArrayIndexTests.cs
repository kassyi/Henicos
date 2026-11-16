#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Array element indexing with <see cref="long"/>, <see cref="ulong"/>, <see cref="uint"/> and narrower integer types
/// matches C# across native CPU, managed and CUDA execution (#1988 (An array element at a long, ulong or uint index
/// fails the native and CUDA build on Windows and wraps to a wrong element elsewhere)).
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class WideArrayIndexTests : TranspilerTestBase
{
    [Scenario(TestScenario.HappyPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllIntegerIndexTypes_ReadWriteCompoundRefStructDeviceAndSequenced_ProduceExpectedValues(
        bool forceManaged)
    {
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseSByteIndexKernel(a, h, 0, 1, 2, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseByteIndexKernel(a, h, 0, 1, 2, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseShortIndexKernel(a, h, 0, 1, 2, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseUShortIndexKernel(a, h, 0, 1, 2, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseCharIndexKernel(a, h, (char)0, (char)1, (char)2, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseIntIndexKernel(a, h, 0, 1, 2, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseUIntIndexKernel(a, h, 0u, 1u, 2u, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseLongIndexKernel(a, h, 0L, 1L, 2L, o));
        AssertIndexType(forceManaged, static (a, h, o) =>
            WideArrayIndexKernels.LaunchExerciseULongIndexKernel(a, h, 0UL, 1UL, 2UL, o));
    }

    // mutant: ElementIndex(long long) with > instead of >= lets index == 3 pass; truncating to unsigned int lets
    // (1L << 32) + 1 read/write a[1]
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false, 3L)]
    [InlineData(true, 3L)]
    [InlineData(false, (1L << 32) + 1)]
    [InlineData(true, (1L << 32) + 1)]
    [InlineData(false, -1L)]
    [InlineData(true, -1L)]
    [InlineData(false, long.MinValue)]
    [InlineData(true, long.MinValue)]
    public void LongIndex_OutOfRangeReadAndWrite_ThrowsIndexOutOfRange(bool forceManaged, long index)
    {
        SkipOnReleaseCuda();
        int[] a = [10, 20, 30];
        int[] o = [-1];

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadLongIndexKernel(a, index, o)));
        Assert.Equal([-1], o);

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchWriteLongIndexKernel(a, index, 99)));
        Assert.Equal([10, 20, 30], a);

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadUncheckedLongIndexKernel(a, index, o)));
        Assert.Equal([-1], o);
    }

    // mutant: ElementIndex(unsigned long long) with > instead of >= lets 3UL pass; truncating to unsigned int lets
    // 1UL << 32 read/write a[0]
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false, 3UL)]
    [InlineData(true, 3UL)]
    [InlineData(false, 1UL << 32)]
    [InlineData(true, 1UL << 32)]
    public void ULongIndex_AboveLengthWithinLongMax_ThrowsIndexOutOfRange(bool forceManaged, ulong index)
    {
        SkipOnReleaseCuda();
        int[] a = [10, 20, 30];
        int[] o = [-1];

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadULongIndexKernel(a, index, o)));
        Assert.Equal([-1], o);

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchWriteULongIndexKernel(a, index, 99)));
        Assert.Equal([10, 20, 30], a);

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadUncheckedULongIndexKernel(a, index, o)));
        Assert.Equal([-1], o);
    }

    // mutant: without operator[](unsigned int), uint.MaxValue converts to int -1 and misses the bounds check
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false, 3u)]
    [InlineData(true, 3u)]
    [InlineData(false, uint.MaxValue)]
    [InlineData(true, uint.MaxValue)]
    [InlineData(false, 0x80000001u)]
    [InlineData(true, 0x80000001u)]
    public void UIntIndex_OutOfRange_ThrowsIndexOutOfRange(bool forceManaged, uint index)
    {
        SkipOnReleaseCuda();
        int[] a = [10, 20, 30];
        int[] o = [-1];

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadUIntIndexKernel(a, index, o)));
        Assert.Equal([-1], o);

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchWriteUIntIndexKernel(a, index, 99)));
        Assert.Equal([10, 20, 30], a);

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadUncheckedUIntIndexKernel(a, index, o)));
        Assert.Equal([-1], o);
    }

    // mutant: ArrayElementIndex.NeedsOverflowCheck returning false throws IndexOutOfRangeException instead of
    // OverflowException
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false, ulong.MaxValue)]
    [InlineData(true, ulong.MaxValue)]
    [InlineData(false, (ulong)long.MaxValue + 1UL)]
    [InlineData(true, (ulong)long.MaxValue + 1UL)]
    public void ULongIndex_AboveLongMax_ThrowsOverflow(bool forceManaged, ulong index)
    {
        SkipOnReleaseCuda();
        int[] a = [10, 20, 30];
        int[] o = [-1];

        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadULongIndexKernel(a, index, o)));
        Assert.Equal([-1], o);

        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchWriteULongIndexKernel(a, index, 99)));
        Assert.Equal([10, 20, 30], a);

        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadUncheckedULongIndexKernel(a, index, o)));
        Assert.Equal([-1], o);
    }

    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false, ulong.MaxValue)]
    [InlineData(true, ulong.MaxValue)]
    public void ULongIndex_LoweredAcrossBarrierOrShuffle_ThrowsOverflow(bool forceManaged, ulong index)
    {
        SkipOnReleaseCuda();
        int[] a = [10, 20, 30];
        int[] o = [-1];

        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadULongIndexAcrossBarrierKernel(a, index, o)));
        Assert.Equal([-1], o);

        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchWriteULongIndexShuffleKernel(a, index, 99)));
        Assert.Equal([10, 20, 30], a);
    }

    [Scenario(TestScenario.HappyPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ULongIndex_LoweredAcrossBarrierOrShuffle_ReadsAndWritesTheElement(bool forceManaged)
    {
        int[] a = [10, 20, 30];
        int[] o = [-1];

        Run(forceManaged, () => WideArrayIndexKernels.LaunchReadULongIndexAcrossBarrierKernel(a, 2UL, o));
        Run(forceManaged, () => WideArrayIndexKernels.LaunchWriteULongIndexShuffleKernel(a, 1UL, 99));

        Assert.Equal([30], o);
        Assert.Equal([10, 99, 30], a);
    }

    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullArray_ULongOverflowAndLongOutOfRange_ThrowsInCSharpOrder(bool forceManaged)
    {
        SkipOnReleaseCuda();
        int[] o = [-1];

        // C# evaluates conv.ovf.i.un on the ulong index before ldelem checks the array reference for null
        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadNullWithULongIndexKernel(null, ulong.MaxValue, o)));
        Assert.Equal([-1], o);

        // A long index has no conversion overflow, so ldelem checks null before bounds
        Assert.Throws<NullReferenceException>(() =>
            Run(forceManaged, () => WideArrayIndexKernels.LaunchReadNullWithLongIndexKernel(null, 1L << 40, o)));
        Assert.Equal([-1], o);

        // Receiver side effects happen before the ulong index conversion throws OverflowException
        int[] a = [10, 20, 30];
        int[] side = [0];
        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () =>
                WideArrayIndexKernels.LaunchReadReceiverSideEffectWithULongIndexKernel(a, ulong.MaxValue, side, o)));
        Assert.Equal([1], side);
        Assert.Equal([-1], o);
    }

    // mutant: ExpressionTraps.MayTrapBeforeStore ignoring System_UInt64 evaluates Side(side) before
    // __gsharp_array_index_u64
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignmentOrder_ULongOverflowAndWideOutOfRange_EvaluatesRhsInCSharpOrder(bool forceManaged)
    {
        SkipOnReleaseCuda();
        int[] a = [10, 20, 30];
        int[] side = [0];

        // conv.ovf.i.un on ulong index runs when evaluating the left target, before the right-hand side
        Assert.Throws<OverflowException>(() =>
            Run(forceManaged, () =>
                WideArrayIndexKernels.LaunchWriteULongIndexWithSideEffectKernel(a, ulong.MaxValue, side)));
        Assert.Equal([0], side);
        Assert.Equal([10, 20, 30], a);

        // A ulong index <= long.MaxValue does not overflow conv.ovf.i.un; stelem checks bounds after the right-hand
        // side
        side = [0];
        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () =>
                WideArrayIndexKernels.LaunchWriteULongIndexWithSideEffectKernel(a, 1UL << 32, side)));
        Assert.Equal([1], side);
        Assert.Equal([10, 20, 30], a);

        // A long index has no conversion overflow; stelem checks bounds after the right-hand side
        side = [0];
        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(forceManaged, () =>
                WideArrayIndexKernels.LaunchWriteLongIndexWithSideEffectKernel(a, 1L << 40, side)));
        Assert.Equal([1], side);
        Assert.Equal([10, 20, 30], a);
    }

    private void AssertIndexType(bool forceManaged, Action<int[], WideIndexHolder, int[]> launch)
    {
        int[] a = [10, 1, 2];
        var h = new WideIndexHolder { Data = [100, 0, 0] };
        int[] o = [-1, -1, -1];

        Run(forceManaged, () => launch(a, h, o));

        Assert.Equal([13, 36, 413], a);
        Assert.Equal([100, 107, 0], h.Data);
        Assert.Equal([10, 36, 14], o);
    }

    // The release CUDA build does not check an index (semantics charter: it keeps three checks); the checked build
    // checks it as C# does
    private void SkipOnReleaseCuda() =>
        Skip.If(IsCudaDialect && !DiffPaths.IsCudaCheckedBuild,
            "The release CUDA build does not check the index; the checked build does and keeps the context usable.");

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
