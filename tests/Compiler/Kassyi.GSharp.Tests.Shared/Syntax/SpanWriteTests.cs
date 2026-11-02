using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>Span&lt;T&gt;</c> / <c>ReadOnlySpan&lt;T&gt;</c> / <c>MemoryExtensions</c> /
/// <c>System.Array</c> write and slice members (#731), comparing G# execution (native CPU, managed CPU, and CUDA)
/// directly against .NET's BCL implementations.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanWriteTests : TranspilerTestBase
{
    private const uint GridDimX = 1;
    private const uint BlockDimX = 1;
    private const string TrapTestVariable = "GSHARP_TEST_CUDA_TRAP";

    public static TheoryData<bool> Paths => new() { false, true };

    public static TheoryData<int, int, bool> SliceWindowCases()
    {
        var data = new TheoryData<int, int, bool>();
        (int Offset, int Count)[] windows = [(0, 0), (5, 0), (20, 0), (0, 1), (7, 1), (19, 1), (0, 2), (3, 5), (2, 16), (1, 17), (0, 20)];
        foreach (var managed in new[] { false, true })
        {
            foreach (var (offset, count) in windows)
            {
                data.Add(offset, count, managed);
            }
        }

        return data;
    }

    public static TheoryData<int, int, int, bool> OverlapCopyCases()
    {
        var data = new TheoryData<int, int, int, bool>();
        (int SrcOffset, int DstOffset, int Count)[] cases =
        [
            (0, 0, 0),
            (4, 4, 0),
            (2, 2, 1),
            (2, 2, 7),
            // Forward overlap: srcOffset < dstOffset < srcOffset + count (requires backward copy)
            (1, 2, 6),
            (1, 3, 6),
            (0, 4, 8),
            // Backward overlap: dstOffset < srcOffset < dstOffset + count (requires forward copy)
            (2, 1, 6),
            (3, 1, 6),
            (4, 0, 8),
            // Disjoint in same buffer
            (0, 10, 6),
            (10, 0, 6),
        ];
        foreach (var managed in new[] { false, true })
        {
            foreach (var (srcOffset, dstOffset, count) in cases)
            {
                data.Add(srcOffset, dstOffset, count, managed);
            }
        }

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Issue731_AsSpanFillReverse_MatchesDotNet(bool managed)
    {
        const uint Threads = 4;
        var data = new int[Threads * 4];
        var expected = new int[Threads * 4];
        for (var tid = 0; tid < Threads; tid++)
        {
            var mine = expected.AsSpan(tid * 4, 4);
            mine.Fill(7);
            mine[0] = 1;
            mine.Reverse();
        }

        Run(managed, () => SpanWriteKernels.LaunchIssue731AsSpanFillReverseKernel(data, gridDimX: 1, blockDimX: Threads));

        Assert.Equal(expected, data);
    }

    // P1 FillSlices: ∀ x, p, k (0 <= p <= p+k <= n), V(p, k) == v and U(p, k) == U_before(p, k)
    [SkippableTheory]
    [MemberData(nameof(SliceWindowCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_P1_FillSlice_ModifiesOnlyTargetWindow(int offset, int count, bool managed)
    {
        var input = CreateSequential(20, start: 100);
        var actual = (int[])input.Clone();
        var expected = (int[])input.Clone();
        expected.AsSpan(offset, count).Fill(-42);

        Run(managed, () => SpanWriteKernels.LaunchFillSliceKernel(actual, offset, count, -42, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    // P1 ClearSlices: ∀ x, p, k (0 <= p <= p+k <= n), V(p, k) == default(T) and U(p, k) == U_before(p, k)
    [SkippableTheory]
    [MemberData(nameof(SliceWindowCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_P1_ClearSlice_ZeroesOnlyTargetWindow(int offset, int count, bool managed)
    {
        var input = CreateSequential(20, start: 50);
        var actual = (int[])input.Clone();
        var expected = (int[])input.Clone();
        expected.AsSpan(offset, count).Clear();

        Run(managed, () => SpanWriteKernels.LaunchClearSliceKernel(actual, offset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(0, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 5, false)]
    [InlineData(0, 0, true)]
    [InlineData(2, 1, true)]
    [InlineData(1, 5, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_P1_ClearStructSlice_ZeroesOnlyTargetWindow(int offset, int count, bool managed)
    {
        var input = new SpanWritePair[8];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = new SpanWritePair { A = i + 1, B = (i + 1) * 10 };
        }

        var actual = (SpanWritePair[])input.Clone();
        var expected = (SpanWritePair[])input.Clone();
        expected.AsSpan(offset, count).Clear();

        Run(managed, () => SpanWriteKernels.LaunchClearStructSliceKernel(actual, offset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackAllocFillClearReverse_MatchesDotNet(bool managed)
    {
        var actual = new int[8];
        int[] expected = [9, 9, 9, 0, 0, 0, 9, 42];

        Run(managed, () => SpanWriteKernels.LaunchStackAllocFillClearReverseKernel(actual, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    // P2 ReverseSlices: ∀ i in 0..k-1, V_after[i] == V_before[k-1-i] and U_after == U_before
    [SkippableTheory]
    [MemberData(nameof(SliceWindowCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_P2_ReverseSlice_ReversesInPlaceAndPreservesOutside(int offset, int count, bool managed)
    {
        var input = CreateSequential(20, start: 1);
        var actual = (int[])input.Clone();
        var expected = (int[])input.Clone();
        expected.AsSpan(offset, count).Reverse();

        // mutant: Types.hpp GSharpArray<T>::Reverse loop bound `Length / 2 - 1` (middle pair of even-length span stays unswapped)
        Run(managed, () => SpanWriteKernels.LaunchReverseSliceKernel(actual, offset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_P2_ReverseVariants_StaticExtensionAndStackArray(bool managed)
    {
        int[] a = [1, 2, 3, 4, 5, 6];
        int[] b = [10, 20, 30, 40, 50];
        var c = new int[5];

        Run(managed, () => SpanWriteKernels.LaunchReverseVariantsKernel(a, b, c, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([6, 5, 4, 3, 2, 1], a);
        Assert.Equal([50, 40, 30, 20, 10], b);
        Assert.Equal([50, 40, 30, 20, 10], c);
    }

    // P3 CopyToOverlap: overlapping source and destination slices on the same buffer behave as memmove
    [SkippableTheory]
    [MemberData(nameof(OverlapCopyCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_P3_CopyToSameBuffer_HandlesOverlapLikeMemmove(int srcOffset, int dstOffset, int count, bool managed)
    {
        var input = CreateSequential(20, start: 10);
        var actual = (int[])input.Clone();
        var expected = (int[])input.Clone();
        expected.AsSpan(srcOffset, count).CopyTo(expected.AsSpan(dstOffset, count));

        // mutant: Types.hpp GSharpArray<T>::TryCopyTo always copies forward (corrupts srcOffset < dstOffset overlap)
        Run(managed, () => SpanWriteKernels.LaunchCopyToSameBufferKernel(actual, srcOffset, dstOffset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(1, 3, 4, false)]
    [InlineData(3, 1, 4, false)]
    [InlineData(1, 3, 4, true)]
    [InlineData(3, 1, 4, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_P3_CopyToStructSameBuffer_HandlesOverlap(int srcOffset, int dstOffset, int count, bool managed)
    {
        var input = new SpanWritePair[10];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = new SpanWritePair { A = i + 1, B = -(i + 1) };
        }

        var actual = (SpanWritePair[])input.Clone();
        var expected = (SpanWritePair[])input.Clone();
        expected.AsSpan(srcOffset, count).CopyTo(expected.AsSpan(dstOffset, count));

        Run(managed, () => SpanWriteKernels.LaunchCopyToStructSameBufferKernel(actual, srcOffset, dstOffset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CopyToVariants_ReadOnlySpanArrayExtensionAndStackArray(bool managed)
    {
        int[] src = [11, 22, 33, 44, 55, 66];
        var dst1 = CreateSequential(8, start: 90);
        var dst2 = CreateSequential(8, start: 80);
        var dst3 = CreateSequential(5, start: 70);

        var expected1 = (int[])dst1.Clone();
        var expected2 = (int[])dst2.Clone();
        var expected3 = (int[])dst3.Clone();
        ((ReadOnlySpan<int>)src).Slice(1, 4).CopyTo(expected1.AsSpan(2, 5));
        src.CopyTo(expected2.AsSpan(1));
        new[] { 100, 200, 300 }.CopyTo(expected3.AsSpan(0, 4));

        Run(managed, () => SpanWriteKernels.LaunchCopyToVariantsKernel(src, dst1, dst2, dst3, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected1, dst1);
        Assert.Equal(expected2, dst2);
        Assert.Equal(expected3, dst3);
    }

    // P4 CopyToShort / TryCopyTo: TryCopyTo returns false when dst.Length < src.Length and leaves dst untouched;
    // returns true when dst.Length >= src.Length
    [SkippableTheory]
    [InlineData(0, 4, 0, 3, false)]
    [InlineData(1, 5, 2, 4, false)]
    [InlineData(2, 1, 0, 0, false)]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(0, 4, 0, 4, false)]
    [InlineData(1, 4, 2, 6, false)]
    [InlineData(0, 4, 0, 3, true)]
    [InlineData(1, 5, 2, 4, true)]
    [InlineData(2, 1, 0, 0, true)]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(0, 4, 0, 4, true)]
    [InlineData(1, 4, 2, 6, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_P4_TryCopyTo_MatchesDotNetAndLeavesShortDestinationUntouched(
        int srcOffset, int srcCount, int dstOffset, int dstCount, bool managed)
    {
        var src = CreateSequential(10, start: 10);
        var dst = CreateSequential(10, start: 100);
        var expectedDst = (int[])dst.Clone();
        var expectedOk = ((ReadOnlySpan<int>)src).Slice(srcOffset, srcCount).TryCopyTo(expectedDst.AsSpan(dstOffset, dstCount));

        var ok = new int[1];
        // mutant: Types.hpp GSharpArray<T>::TryCopyTo `Length > destination.Length` -> `Length >= destination.Length` (exact fit fails)
        Run(managed, () => SpanWriteKernels.LaunchTryCopyToSliceKernel(src, srcOffset, srcCount, dst, dstOffset, dstCount, ok,
            gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedOk ? 1 : 0, ok[0]);
        Assert.Equal(expectedDst, dst);
    }

    [SkippableTheory]
    [InlineData(1, 5, 3, 4, false)]
    [InlineData(1, 5, 3, 5, false)]
    [InlineData(3, 5, 1, 5, false)]
    [InlineData(1, 5, 3, 4, true)]
    [InlineData(1, 5, 3, 5, true)]
    [InlineData(3, 5, 1, 5, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_P4_TryCopyToSameBuffer_HandlesOverlapAndShortDestination(
        int srcOffset, int srcCount, int dstOffset, int dstCount, bool managed)
    {
        var actual = CreateSequential(12, start: 1);
        var expected = (int[])actual.Clone();
        var expectedOk = expected.AsSpan(srcOffset, srcCount).TryCopyTo(expected.AsSpan(dstOffset, dstCount));

        var ok = new int[1];
        Run(managed, () => SpanWriteKernels.LaunchTryCopyToSameBufferKernel(actual, srcOffset, srcCount, dstOffset, dstCount, ok,
            gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedOk ? 1 : 0, ok[0]);
        Assert.Equal(expected, actual);
    }

    // P4 CopyToShort on CPU: throws ArgumentException and leaves destination untouched
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_P4_CopyTo_DestinationTooShort_ThrowsArgumentExceptionAndPreservesDestination(bool managed)
    {
        Skip.If(IsCudaDialect, "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] src = [1, 2, 3, 4];
        int[] dst = [90, 91, 92];
        var before = (int[])dst.Clone();

        Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanWriteKernels.LaunchCopyToDirectKernel(src, dst, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal(before, dst);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_P4_CopyTo_DestinationTooShort_CudaTrapsWhenEnabled()
    {
        Skip.If(!IsCudaDialect || Environment.GetEnvironmentVariable(TrapTestVariable) != "1",
            $"Run alone with {TrapTestVariable}=1; a CUDA trap terminates the process context.");

        int[] src = [1, 2, 3, 4];
        int[] dst = [90, 91, 92];
        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            SpanWriteKernels.LaunchCopyToDirectKernel(src, dst, gridDimX: GridDimX, blockDimX: BlockDimX));
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AsSpanOverloads_StartAndLength_MatchDotNet(bool managed)
    {
        int[] data = [10, 20, 30, 40, 50, 60];
        var results = new int[4];

        Run(managed, () => SpanWriteKernels.LaunchAsSpanOverloadsKernel(data, 2, 3, results, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([6, 4, 3, 0], results);
        Assert.Equal([10, 20, 130, 40, 50, 60], data);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SpanEmptyAndIsEmpty_MatchesDotNet(bool managed)
    {
        int[] data = [7, 8, 9];
        var results = new int[4];

        Run(managed, () => SpanWriteKernels.LaunchSpanEmptyAndIsEmptyKernel(data, results, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([1, 1, 1, 1], results);
        Assert.Equal([7, 8, 9], data);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ArrayFillAndClear_MatchesDotNet(bool managed)
    {
        var a = CreateSequential(6, start: 1);
        var b = CreateSequential(6, start: 1);
        var c = CreateSequential(6, start: 1);
        var d = CreateSequential(6, start: 1);

        var expectedA = (int[])a.Clone();
        var expectedB = (int[])b.Clone();
        var expectedC = (int[])c.Clone();
        var expectedD = (int[])d.Clone();
        Array.Fill(expectedA, 5);
        Array.Fill(expectedB, 8, 2, 3);
        Array.Clear(expectedC);
        Array.Clear(expectedD, 1, 4);

        Run(managed, () => SpanWriteKernels.LaunchArrayFillAndClearKernel(a, b, c, d, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedA, a);
        Assert.Equal(expectedB, b);
        Assert.Equal(expectedC, c);
        Assert.Equal(expectedD, d);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ArrayReverse_FullAndRange_MatchesDotNet(bool managed)
    {
        var a = CreateSequential(7, start: 1);
        var b = CreateSequential(8, start: 10);
        var expectedA = (int[])a.Clone();
        var expectedB = (int[])b.Clone();
        Array.Reverse(expectedA);
        Array.Reverse(expectedB, 2, 5);

        Run(managed, () => SpanWriteKernels.LaunchArrayReverseKernel(a, b, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedA, a);
        Assert.Equal(expectedB, b);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ArrayCopy_3And5Arg_MatchesDotNet(bool managed)
    {
        var src = CreateSequential(8, start: 10);
        var dst3 = CreateSequential(8, start: 100);
        var dst5 = CreateSequential(8, start: 200);
        var expected3 = (int[])dst3.Clone();
        var expected5 = (int[])dst5.Clone();
        Array.Copy(src, expected3, 5);
        Array.Copy(src, 2, expected5, 3, 4);

        Run(managed, () => SpanWriteKernels.LaunchArrayCopy3Kernel(src, dst3, 5, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArrayCopy5Kernel(src, 2, dst5, 3, 4, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected3, dst3);
        Assert.Equal(expected5, dst5);
    }

    [SkippableTheory]
    [MemberData(nameof(OverlapCopyCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ArrayCopySameBuffer_HandlesOverlapLikeDotNet(int srcIdx, int dstIdx, int length, bool managed)
    {
        var actual = CreateSequential(20, start: 1);
        var expected = (int[])actual.Clone();
        Array.Copy(expected, srcIdx, expected, dstIdx, length);

        Run(managed, () => SpanWriteKernels.LaunchArrayCopySameBufferKernel(actual, srcIdx, dstIdx, length, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ArraySort_KeysAndPairs_MatchesDotNet(bool managed)
    {
        int[] keys1 = [9, -2, 5, 0, 5, -10, 3];
        var expectedKeys1 = (int[])keys1.Clone();
        Array.Sort(expectedKeys1);

        float[] floatKeys = [float.NaN, 1f, -0f, 0f, float.NegativeInfinity, float.NaN, -2f];
        var expectedFloatKeys = (float[])floatKeys.Clone();
        Array.Sort(expectedFloatKeys);

        int[] keys2 = [3, 1, 2, 1, 3, 2];
        int[] items2 = [30, 10, 20, 11, 31, 21];
        var expectedKeys2 = (int[])keys2.Clone();
        var expectedItems2 = (int[])items2.Clone();
        Array.Sort(expectedKeys2, expectedItems2);

        Run(managed, () => SpanWriteKernels.LaunchArraySortKeysKernel(keys1, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArraySortFloatKeysKernel(floatKeys, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArraySortPairsKernel(keys2, items2, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedKeys1, keys1);
        Assert.Equal(
            Array.ConvertAll(expectedFloatKeys, BitConverter.SingleToInt32Bits),
            Array.ConvertAll(floatKeys, BitConverter.SingleToInt32Bits));
        Assert.Equal(expectedKeys2, keys2);
        Assert.Equal(expectedItems2, items2);
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_ArraySortPairs_LengthMismatch_ThrowsArgumentException(bool managed)
    {
        Skip.If(IsCudaDialect, "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanWriteKernels.LaunchArraySortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EvaluationOrder_LeftToRightAndSingleEvaluation(bool managed)
    {
        var src = CreateSequential(8, start: 10);
        var dst1 = new int[4];
        var dst2 = new int[6];
        var dst3 = new int[6];
        var counters = new int[4];

        var expectedDst1 = new int[4];
        var expectedDst2 = new int[6];
        var expectedDst3 = new int[6];
        var expectedCounters = new int[4];
        {
            var i = 1;
            src.AsSpan().Slice(i++, i++).CopyTo(expectedDst1.AsSpan());
            expectedCounters[0] = i;

            var j = 1;
            Array.Fill(expectedDst2, j * 10, j++, ++j);
            expectedCounters[1] = j;

            var k = 0;
            var len = 2;
            Array.Copy(src, k++, expectedDst3, k++, ++len);
            expectedCounters[2] = k;
            expectedCounters[3] = len;
        }

        Run(managed, () => SpanWriteKernels.LaunchEvaluationOrderSpanWriteKernel(src, dst1, dst2, dst3, counters,
            gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedCounters, counters);
        Assert.Equal(expectedDst1, dst1);
        Assert.Equal(expectedDst2, dst2);
        Assert.Equal(expectedDst3, dst3);
    }

    private void Run(bool managed, Action launch)
    {
        Skip.If(managed && IsCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }

    private static int[] CreateSequential(int length, int start)
    {
        var array = new int[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = start + i;
        }

        return array;
    }
}
