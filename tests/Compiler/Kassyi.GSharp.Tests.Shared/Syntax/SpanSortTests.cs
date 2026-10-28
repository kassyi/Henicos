using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>MemoryExtensions.Sort</c> in device code (#625): the port of the .NET introsort in <c>Prelude/Sort.hpp</c> must give
/// the same order as .NET, which the managed path runs itself. Expected values come from the host calling .NET on a copy of
/// the input the test holds, never from the device.
/// </summary>
/// <remarks>
/// Properties: (1) the result equals .NET element by element (for floating point, bit by bit, so the order of -0.0 and 0.0
/// and of NaN payloads is checked); (2) adjacent elements are in order by <see cref="Comparer{T}.Default"/>, which does not
/// depend on the .NET sort. Each input runs on the CPU dialect natively and on the managed path, and on CUDA.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanSortTests : TranspilerTestBase
{
    private const uint GridDimX = 2;
    private const uint BlockDimX = 32;

    // Lengths around the insertion sort threshold (16), the 2- and 3-element paths and larger partitions
    private static readonly int[] s_lengths = [1, 2, 3, 4, 15, 16, 17, 31, 32, 33, 64, 100, 1000];

    public static TheoryData<int, SortPattern, bool> IntCases()
    {
        var data = new TheoryData<int, SortPattern, bool>();
        foreach (var managed in new[] { false, true })
        {
            foreach (var length in s_lengths)
            {
                foreach (var pattern in Enum.GetValues<SortPattern>())
                {
                    data.Add(length, pattern, managed);
                }
            }
        }

        return data;
    }

    public static TheoryData<int, bool> TypedCases()
    {
        var data = new TheoryData<int, bool>();
        foreach (var managed in new[] { false, true })
        {
            foreach (var length in new[] { 1, 2, 3, 16, 17, 33, 100, 1000 })
            {
                data.Add(length, managed);
            }
        }

        return data;
    }

    public static TheoryData<bool> Paths() => new() { false, true };

    [SkippableTheory]
    [MemberData(nameof(IntCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortInt_MatchesDotNet(int length, SortPattern pattern, bool managed)
    {
        var input = SortInputs.Int(length, pattern, seed: 625 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_insertion `j >= 0` -> `j > 0` (the first element never moves; descending, length 4)
        AssertSameAsDotNet(input, data, $"int length {length} {pattern}");
    }

    // The input that McIlroy's adversary builds against the .NET introsort exhausts the depth limit, so the heap sort runs
    [SkippableTheory]
    [InlineData(64, false)]
    [InlineData(1000, false)]
    [InlineData(64, true)]
    [InlineData(1000, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortInt_DepthLimitInput_UsesHeapSortAndMatchesDotNet(int length, bool managed)
    {
        var input = SortInputs.HeapSortKiller(length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_down_heap `child < n` -> `child <= n` (reads past the heap; order breaks)
        AssertSameAsDotNet(input, data, $"heap sort input of length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortLong_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6251 + length);
        var input = new long[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = random.NextInt64(long.MinValue, long.MaxValue) >> random.Next(0, 63);
        }

        var data = (long[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortLongKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"long length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortUInt_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6252 + length);
        var input = new uint[length];
        for (var i = 0; i < length; i++)
        {
            // Values above int.MaxValue order differently as signed integers
            input[i] = (uint)random.NextInt64(0, uint.MaxValue + 1L);
        }

        var data = (uint[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortUIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"uint length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortChar_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6253 + length);
        var input = new char[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = (char)random.Next(0, char.MaxValue + 1);
        }

        var data = (char[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortCharKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"char length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortEnum_OrdersByUnderlyingValue(int length, bool managed)
    {
        SortRank[] ranks = [SortRank.High, SortRank.Low, SortRank.Middle];
        var random = new Random(6254 + length);
        var input = new SortRank[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = ranks[random.Next(ranks.Length)];
        }

        var data = (SortRank[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortEnumKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, $"enum length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortFloat_NaNFirstAndSignedZerosInDotNetOrder(int length, bool managed)
    {
        var input = SortInputs.Single(length, seed: 6255 + length);
        var data = (float[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortFloatKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_default without __gsharp_sort_move_nans_to_front (NaN is not first)
        AssertSameBits(input, data, BitConverter.SingleToInt32Bits, $"float length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortDouble_NaNFirstAndSignedZerosInDotNetOrder(int length, bool managed)
    {
        var input = SortInputs.Double(length, seed: 6256 + length);
        var data = (double[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortDoubleKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameBits(input, data, BitConverter.DoubleToInt64Bits, $"double length {length}");
    }

    [SkippableTheory]
    [InlineData(new[] { float.NaN, 1f, float.NaN, -0f, 0f, float.NegativeInfinity, float.PositiveInfinity, 2f }, false)]
    [InlineData(new[] { float.NaN, 1f, float.NaN, -0f, 0f, float.NegativeInfinity, float.PositiveInfinity, 2f }, true)]
    [InlineData(new[] { float.NaN, float.NaN, float.NaN }, false)]
    [InlineData(new[] { float.NaN, float.NaN, float.NaN }, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortFloat_SpecialValues(float[] input, bool managed)
    {
        var data = (float[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortFloatKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameBits(input, data, BitConverter.SingleToInt32Bits, "special values");
    }

    // Each of the 64 threads sorts a different segment at the same time
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortSegments_EveryThreadSortsItsOwnBuffer(bool managed)
    {
        const int Segments = (int)(GridDimX * BlockDimX);
        const int Length = SpanSortKernels.SegmentLength;
        var random = new Random(6257);
        var input = new int[Segments * Length];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = random.Next(-50, 50);
        }

        var data = (int[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortSegmentsKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        for (var s = 0; s < Segments; s++)
        {
            AssertSameAsDotNet(input.AsSpan(s * Length, Length).ToArray(), data.AsSpan(s * Length, Length).ToArray(),
                $"segment {s}");
        }
    }

    // MaskedComparer leaves many keys equal: the order of equal keys must be the one .NET picks
    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortComparerStruct_MatchesDotNet(int length, bool managed)
    {
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6258 + length);
        var data = (int[])input.Clone();
        var comparer = new MaskedComparer { Mask = 7 };

        Run(managed, () => SpanSortKernels.LaunchSortComparerKernel(data, 7, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: MemoryExtensionsTranspilerRule writes __gsharp_sort without the comparer (the #625 bug)
        var expected = (int[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan(), comparer);
        AssertSameOrder(expected, data, comparer.Compare, $"comparer length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortComparerStruct_ExtensionCall_SortsDescending(bool managed)
    {
        int[] data = [1, 3, 2];

        Run(managed, () => SpanSortKernels.LaunchSortDescendingKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([3, 2, 1], data);
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortComparisonLambda_MatchesDotNet(int length, bool managed)
    {
        const int Mask = 5;
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6259 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortComparisonKernel(data, Mask, gridDimX: GridDimX, blockDimX: BlockDimX));

        Comparison<int> comparison = static (a, b) => (a & Mask) < (b & Mask) ? -1 : ((a & Mask) > (b & Mask) ? 1 : 0);
        var expected = (int[])input.Clone();
        expected.AsSpan().Sort(comparison);
        AssertSameOrder(expected, data, comparison, $"comparison length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortComparisonMethodGroup_MatchesDotNet(int length, bool managed)
    {
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6260 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortMethodGroupKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        var expected = (int[])input.Clone();
        expected.AsSpan().Sort(SpanSortKernels.CompareLowNibble);
        AssertSameOrder(expected, data, SpanSortKernels.CompareLowNibble, $"method group length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortNullComparer_UsesTheDefaultOrder(bool managed)
    {
        var input = SortInputs.Int(100, SortPattern.FewDistinct, seed: 6261);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortNullComparerKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, "null comparer");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortComparableStruct_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6262 + length);
        var input = new SortRecord[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = new SortRecord { Key = random.Next(0, 4), Tag = i };
        }

        var data = (SortRecord[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortComparableKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // The tags show the order of equal keys
        var expected = (SortRecord[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());
        AssertSameOrder(expected, data, static (a, b) => a.CompareTo(b), $"comparable length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairs_MatchesDotNet(int length, bool managed)
    {
        var keys = SortInputs.Int(length, SortPattern.FewDistinct, seed: 6263 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsKernel(dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_items::swap does nothing (the items stay where they were, the #625 bug)
        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, Comparer<int>.Default.Compare,
            $"pairs length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortPairsFloat_MovesItemsWithNaNAndSignedZeros(int length, bool managed)
    {
        var keys = SortInputs.Single(length, seed: 6264 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((float[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsFloatKernel(dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_move_nans_to_front without items.swap (a NaN key leaves its item behind)
        var (expectedKeys, expectedItems) = ((float[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        Assert.Equal(Array.ConvertAll(expectedKeys, BitConverter.SingleToInt32Bits), Array.ConvertAll(dataKeys, BitConverter.SingleToInt32Bits));
        Assert.Equal(expectedItems, dataItems);
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairsComparerStruct_MatchesDotNet(int length, bool managed)
    {
        var keys = SortInputs.Int(length, SortPattern.Random, seed: 6265 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());
        var comparer = new MaskedComparer { Mask = 3 };

        Run(managed, () => SpanSortKernels.LaunchSortPairsComparerKernel(dataKeys, dataItems, 3, gridDimX: GridDimX, blockDimX: BlockDimX));

        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan(), comparer);
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, comparer.Compare,
            $"pairs comparer length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairsComparison_MatchesDotNet(int length, bool managed)
    {
        const int Mask = 6;
        var keys = SortInputs.Int(length, SortPattern.Random, seed: 6266 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsComparisonKernel(dataKeys, dataItems, Mask, gridDimX: GridDimX, blockDimX: BlockDimX));

        Comparison<int> comparison = static (a, b) => (a & Mask) < (b & Mask) ? -1 : ((a & Mask) > (b & Mask) ? 1 : 0);
        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan(), comparison);
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, comparison,
            $"pairs comparison length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(TypedCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairsComparableStruct_MatchesDotNet(int length, bool managed)
    {
        var random = new Random(6267 + length);
        var keys = new SortRecord[length];
        for (var i = 0; i < length; i++)
        {
            keys[i] = new SortRecord { Key = random.Next(0, 4), Tag = -i };
        }

        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((SortRecord[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsComparableKernel(dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        var (expectedKeys, expectedItems) = ((SortRecord[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, static (a, b) => a.CompareTo(b),
            $"pairs comparable length {length}");
    }

    // .NET: ArgumentException (Argument_SpansMustHaveSameLength) before sorting
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SortPairs_LengthMismatch_ThrowsArgumentException(bool managed)
    {
        Skip.If(IsCudaDialect, "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        // mutant: Sort.hpp __gsharp_sort_same_length returns true (no exception; the items are read past their end)
        Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanSortKernels.LaunchSortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
    }

    // An inconsistent comparer runs the partition scan past the partition, which .NET reports as ArgumentException
    // (BadComparer). Length 16 sorts by insertion only, which never reads past the span: neither .NET nor the port throws.
    [SkippableTheory]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SortBogusComparer_ThrowsArgumentExceptionAsDotNet(int length, bool managed)
    {
        Skip.If(IsCudaDialect, "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6268);
        var data = (int[])input.Clone();
        var expected = (int[])input.Clone();
        var dotNetThrows = Record.Exception(() => MemoryExtensions.Sort(expected.AsSpan(), new AlwaysLessComparer())) is ArgumentException;
        Assert.Equal(length > 16, dotNetThrows);

        // mutant: Sort.hpp __gsharp_sort_bad_comparer traps with InvalidOperation instead of Argument (wrong exception type)
        var exception = Record.Exception(() => Run(managed,
            () => SpanSortKernels.LaunchSortBogusComparerKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal(dotNetThrows, exception is ArgumentException);
        if (!dotNetThrows)
        {
            Assert.Equal(expected, data);
        }
    }

    // The depth-limit input with every value halved: equal keys reach the heap sort, so the items (the input positions)
    // show whether the heap sort moves equal keys as .NET does
    [SkippableTheory]
    [InlineData(64, false)]
    [InlineData(1000, false)]
    [InlineData(64, true)]
    [InlineData(1000, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortPairs_DepthLimitInputWithEqualKeys_MatchesDotNet(int length, bool managed)
    {
        var keys = Array.ConvertAll(SortInputs.HeapSortKiller(length), static v => v / 2);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsKernel(dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        // Reaching the heap sort: the mutant below changes only the heap sort, and these inputs kill it
        // mutant: Sort.hpp __gsharp_sort_down_heap `compare(keys[child - 1], keys[child]) < 0` -> `<= 0` (equal keys move)
        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, Comparer<int>.Default.Compare,
            $"heap sort pairs of length {length}");
    }

    [SkippableTheory]
    [InlineData(64, false)]
    [InlineData(1000, false)]
    [InlineData(64, true)]
    [InlineData(1000, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortComparable_DepthLimitInputWithEqualKeys_MatchesDotNet(int length, bool managed)
    {
        var values = SortInputs.HeapSortKiller(length);
        var input = new SortRecord[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = new SortRecord { Key = values[i] / 2, Tag = i };
        }

        var data = (SortRecord[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortComparableKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_down_heap `compare(keys[child - 1], keys[child]) < 0` -> `<= 0` (equal keys move)
        var expected = (SortRecord[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());
        AssertSameOrder(expected, data, static (a, b) => a.CompareTo(b), $"heap sort comparable of length {length}");
    }

    // An inconsistent CompareTo of struct keys sorted with items: .NET's default path for keys and items has no stop at the
    // ends of the partition, so the scan runs past it and .NET throws ArgumentException
    [SkippableTheory]
    [InlineData(17, false)]
    [InlineData(32, false)]
    [InlineData(100, false)]
    [InlineData(17, true)]
    [InlineData(32, true)]
    [InlineData(100, true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SortPairsComparable_InconsistentCompareTo_ThrowsArgumentExceptionAsDotNet(int length, bool managed)
    {
        Skip.If(IsCudaDialect, "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var keys = new InconsistentRecord[length];
        var items = Enumerable.Range(0, length).ToArray();
        var dotNetKeys = (InconsistentRecord[])keys.Clone();
        var dotNetItems = (int[])items.Clone();
        Assert.IsType<ArgumentException>(Record.Exception(() => MemoryExtensions.Sort(dotNetKeys.AsSpan(), dotNetItems.AsSpan())));

        // mutant: Sort.hpp __gsharp_sort_pairs_comparable with the Guarded scan (stops at the ends; no exception)
        var exception = Record.Exception(() => Run(managed,
            () => SpanSortKernels.LaunchSortPairsInconsistentKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.IsType<ArgumentException>(exception);
    }

    // .NET wraps an exception thrown by the comparison in InvalidOperationException (ElementAt throws
    // ArgumentOutOfRangeException here); the native CPU path records the trap inside the comparison the same way
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SortComparison_ExceptionInComparison_ThrowsInvalidOperationException(bool managed)
    {
        Skip.If(IsCudaDialect, "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] data = [2, 99, 1, 0];
        int[] table = [10, 20, 30];

        // mutant: ErrorChannel.hpp __gsharp_trap without the translation inside a comparison (ArgumentOutOfRangeException)
        var exception = Record.Exception(() => Run(managed,
            () => SpanSortKernels.LaunchSortTrappingComparisonKernel(data, table, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.IsType<InvalidOperationException>(exception);
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

    private static void AssertSameAsDotNet<T>(T[] input, T[] actual, string what)
    {
        var expected = (T[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());

        // (1) the order of .NET, element by element
        Assert.True(expected.AsSpan().SequenceEqual(actual), $"{what}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

        // (2) ascending by the default comparer, which does not depend on the order .NET picks among equal keys
        var comparer = Comparer<T>.Default;
        for (var i = 1; i < actual.Length; i++)
        {
            Assert.True(comparer.Compare(actual[i - 1], actual[i]) <= 0, $"{what}: elements {i - 1} and {i} are out of order");
        }
    }

    private static void AssertSameOrder<T>(T[] expected, T[] actual, Comparison<T> comparison, string what)
    {
        // (1) the order of .NET with the same comparison, element by element (equal keys included)
        Assert.True(expected.AsSpan().SequenceEqual(actual), $"{what}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

        // (2) ascending by the comparison
        for (var i = 1; i < actual.Length; i++)
        {
            Assert.True(comparison(actual[i - 1], actual[i]) <= 0, $"{what}: elements {i - 1} and {i} are out of order");
        }
    }

    private static void AssertSamePairs<TKey>(TKey[] keys, int[] items, TKey[] expectedKeys, int[] expectedItems,
        TKey[] actualKeys, int[] actualItems, Comparison<TKey> comparison, string what)
    {
        // (1) keys and items in the order of .NET
        AssertSameOrder(expectedKeys, actualKeys, comparison, what);
        Assert.True(expectedItems.AsSpan().SequenceEqual(actualItems), $"{what}: items expected [{string.Join(", ", expectedItems)}], got [{string.Join(", ", actualItems)}]");

        // (3) each item still sits with its own key (items are the input positions): a permutation of the input pairs
        for (var i = 0; i < actualItems.Length; i++)
        {
            Assert.Equal(keys[actualItems[i]], actualKeys[i]);
        }

        Assert.Equal(items.Length, actualItems.Distinct().Count());
    }

    // Equal keys of floating point (-0.0 and 0.0, NaNs with different payloads) differ in their bits, so the bits show
    // whether the port moves them exactly as .NET does
    private static void AssertSameBits<T, TBits>(T[] input, T[] actual, Func<T, TBits> bits, string what)
        where TBits : IEquatable<TBits>
    {
        var expected = (T[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());
        var expectedBits = Array.ConvertAll(expected, x => bits(x));
        var actualBits = Array.ConvertAll(actual, x => bits(x));
        Assert.True(expectedBits.AsSpan().SequenceEqual(actualBits),
            $"{what}: expected [{string.Join(", ", expectedBits)}], got [{string.Join(", ", actualBits)}]");
    }
}

/// <summary>The value patterns of <see cref="SortInputs.Int"/>.</summary>
public enum SortPattern
{
    Random,
    FewDistinct,
    Ascending,
    Descending,
    AllEqual,
}

/// <summary>Deterministic sort inputs (fixed seeds).</summary>
internal static class SortInputs
{
    public static int[] Int(int length, SortPattern pattern, int seed)
    {
        var random = new Random(seed);
        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pattern switch
            {
                SortPattern.Random => random.Next(int.MinValue, int.MaxValue),
                SortPattern.FewDistinct => random.Next(0, 4),
                SortPattern.Ascending => i,
                SortPattern.Descending => length - i,
                _ => 7,
            };
        }

        return values;
    }

    // Many equal keys with different bits: -0.0 / 0.0 and NaNs with different payloads, mixed with a few ordinary values
    public static float[] Single(int length, int seed)
    {
        float[] pool =
        [
            -0f, 0f, 1f, -1f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
            BitConverter.Int32BitsToSingle(0x7fc00001), BitConverter.Int32BitsToSingle(unchecked((int)0xffc00002)),
        ];
        var random = new Random(seed);
        var values = new float[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pool[random.Next(pool.Length)];
        }

        return values;
    }

    public static double[] Double(int length, int seed)
    {
        double[] pool =
        [
            -0d, 0d, 1d, -1d, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            BitConverter.Int64BitsToDouble(0x7ff8000000000001), BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000002)),
        ];
        var random = new Random(seed);
        var values = new double[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pool[random.Next(pool.Length)];
        }

        return values;
    }

    /// <summary>
    /// McIlroy's adversary ("A Killer Adversary for Quicksort", 1999) run against the .NET introsort: the values are fixed
    /// lazily, so that each partition peels off only a few elements and the depth limit runs out (the heap sort path).
    /// </summary>
    public static int[] HeapSortKiller(int length)
    {
        var gas = length;
        var values = new int[length];
        Array.Fill(values, gas);
        var order = new int[length];
        for (var i = 0; i < length; i++)
        {
            order[i] = i;
        }

        var solid = 0;
        var candidate = 0;
        MemoryExtensions.Sort(order.AsSpan(), (x, y) =>
        {
            if (values[x] == gas && values[y] == gas)
            {
                values[x == candidate ? x : y] = solid++;
            }

            if (values[x] == gas)
            {
                candidate = x;
            }
            else if (values[y] == gas)
            {
                candidate = y;
            }

            return values[x].CompareTo(values[y]);
        });

        // The values never compared with each other while gas are larger than every frozen one, in any order
        for (var i = 0; i < length; i++)
        {
            if (values[i] == gas)
            {
                values[i] = solid++;
            }
        }

        return values;
    }
}
