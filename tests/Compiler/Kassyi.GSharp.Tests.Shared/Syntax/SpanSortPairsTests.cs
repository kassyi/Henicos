using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanSortTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>MemoryExtensions.Sort</c> for key-value pairs in device code (#625).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanSortPairsTests : SpanKernelPathTestBase
{
    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairs_MatchesDotNet(int length, bool managed)
    {
        var keys = SortInputs.Int(length, SortPattern.FewDistinct, seed: 6263 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsKernel(
            dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_items::swap does nothing (the items stay where they were, the #625 bug)
        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, Comparer<int>.Default.Compare,
            $"pairs length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SortPairsFloat_MovesItemsWithNaNAndSignedZeros(int length, bool managed)
    {
        var keys = SortInputs.Single(length, seed: 6264 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((float[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsFloatKernel(
            dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_move_nans_to_front without items.swap (a NaN key leaves its item behind)
        var (expectedKeys, expectedItems) = ((float[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        Assert.Equal(
            Array.ConvertAll(expectedKeys, BitConverter.SingleToInt32Bits),
            Array.ConvertAll(dataKeys, BitConverter.SingleToInt32Bits));
        Assert.Equal(expectedItems, dataItems);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairsComparerStruct_MatchesDotNet(int length, bool managed)
    {
        var keys = SortInputs.Int(length, SortPattern.Random, seed: 6265 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());
        var comparer = new MaskedComparer { Mask = 3 };

        Run(managed, () => SpanSortKernels.LaunchSortPairsComparerKernel(
            dataKeys, dataItems, 3, gridDimX: GridDimX, blockDimX: BlockDimX));

        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan(), comparer);
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, comparer.Compare,
            $"pairs comparer length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SortPairsComparison_MatchesDotNet(int length, bool managed)
    {
        const int Mask = 6;
        var keys = SortInputs.Int(length, SortPattern.Random, seed: 6266 + length);
        var items = Enumerable.Range(0, length).ToArray();
        var (dataKeys, dataItems) = ((int[])keys.Clone(), (int[])items.Clone());

        Run(managed, () => SpanSortKernels.LaunchSortPairsComparisonKernel(
            dataKeys, dataItems, Mask, gridDimX: GridDimX, blockDimX: BlockDimX));

        Comparison<int> comparison = static (a, b) => (a & Mask) < (b & Mask) ? -1 : ((a & Mask) > (b & Mask) ? 1 : 0);
        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan(), comparison);
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, comparison,
            $"pairs comparison length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
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

        Run(managed, () => SpanSortKernels.LaunchSortPairsComparableKernel(
            dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        var (expectedKeys, expectedItems) = ((SortRecord[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, static (a, b) => a.CompareTo(b),
            $"pairs comparable length {length}");
    }

    // .NET: ArgumentException (Argument_SpansMustHaveSameLength) before sorting
    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SortPairs_LengthMismatch_ThrowsArgumentException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        // mutant: Sort.hpp __gsharp_sort_same_length returns true (no exception; the items are read past their end)
        Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanSortKernels.LaunchSortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
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

        Run(managed, () => SpanSortKernels.LaunchSortPairsKernel(
            dataKeys, dataItems, gridDimX: GridDimX, blockDimX: BlockDimX));

        // Reaching the heap sort: the mutant below changes only the heap sort, and these inputs kill it
        // mutant: Sort.hpp __gsharp_sort_down_heap `compare(keys[child - 1], keys[child]) < 0` -> `<= 0`
        // (equal keys move)
        var (expectedKeys, expectedItems) = ((int[])keys.Clone(), (int[])items.Clone());
        MemoryExtensions.Sort(expectedKeys.AsSpan(), expectedItems.AsSpan());
        AssertSamePairs(keys, items, expectedKeys, expectedItems, dataKeys, dataItems, Comparer<int>.Default.Compare,
            $"heap sort pairs of length {length}");
    }

    // An inconsistent CompareTo of struct keys sorted with items: .NET's default path for keys and items has no stop
    // at the ends of the partition, so the scan runs past it and .NET throws ArgumentException
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
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var keys = new InconsistentRecord[length];
        var items = Enumerable.Range(0, length).ToArray();
        var dotNetKeys = (InconsistentRecord[])keys.Clone();
        var dotNetItems = (int[])items.Clone();
        Assert.IsType<ArgumentException>(
            Record.Exception(() => MemoryExtensions.Sort(dotNetKeys.AsSpan(), dotNetItems.AsSpan())));

        // mutant: Sort.hpp __gsharp_sort_pairs_comparable with the Guarded scan (stops at the ends; no exception)
        var exception = Record.Exception(() => Run(managed,
            () => SpanSortKernels.LaunchSortPairsInconsistentKernel(
                keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.IsType<ArgumentException>(exception);
    }
}
