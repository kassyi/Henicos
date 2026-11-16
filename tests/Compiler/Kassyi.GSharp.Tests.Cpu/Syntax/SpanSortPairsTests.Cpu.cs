#nullable enable
using System;
using System.Linq;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanSortTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanSortPairsTests
{
    // .NET: ArgumentException (Argument_SpansMustHaveSameLength) before sorting
    [Theory]
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void SortPairs_MismatchedKeysAndItemsLength_ThrowsArgumentException(bool managed)
    {
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        // mutant: Sort.hpp __gsharp_sort_same_length returns true (no exception; the items are read past their end)
        Assert.Throws<ArgumentException>(() => Run(
            managed,
            () => SpanSortKernels.LaunchSortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
    }

    // An inconsistent CompareTo of struct keys sorted with items: .NET's default path for keys and items has no stop
    // at the ends of the partition, so the scan runs past it and .NET throws ArgumentException
    [Theory]
    [InlineData(17, false)]
    [InlineData(32, false)]
    [InlineData(100, false)]
    [InlineData(17, true)]
    [InlineData(32, true)]
    [InlineData(100, true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void SortPairs_InconsistentCompareToStructKeys_ThrowsArgumentException(int length, bool managed)
    {
        var keys = new InconsistentRecord[length];
        var items = Enumerable.Range(0, length).ToArray();
        var dotNetKeys = (InconsistentRecord[])keys.Clone();
        var dotNetItems = (int[])items.Clone();
        Assert.IsType<ArgumentException>(
            Record.Exception(() => MemoryExtensions.Sort(dotNetKeys.AsSpan(), dotNetItems.AsSpan())));

        // mutant: Sort.hpp __gsharp_sort_pairs_comparable with the Guarded scan (stops at the ends; no exception)
        var exception = Record.Exception(() => Run(
            managed,
            () => SpanSortKernels.LaunchSortPairsInconsistentKernel(
                keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.IsType<ArgumentException>(exception);
    }
}
