using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanSortTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>MemoryExtensions.Sort</c> with custom comparers and comparisons in device code (#625).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanSortCustomOrderTests : SpanKernelPathTestBase
{
    // MaskedComparer leaves many keys equal: the order of equal keys must be the one .NET picks
    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Sort_ComparerStruct_MatchesDotNetOrder(int length, bool managed)
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
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Sort_DescendingComparerStruct_SortsElementsDescending(bool managed)
    {
        int[] data = [1, 3, 2];

        Run(managed, () => SpanSortKernels.LaunchSortDescendingKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([3, 2, 1], data);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Sort_ComparisonLambda_MatchesDotNetOrder(int length, bool managed)
    {
        const int Mask = 5;
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6259 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortComparisonKernel(
            data, Mask, gridDimX: GridDimX, blockDimX: BlockDimX));

        Comparison<int> comparison = static (a, b) => (a & Mask) < (b & Mask) ? -1 : ((a & Mask) > (b & Mask) ? 1 : 0);
        var expected = (int[])input.Clone();
        expected.AsSpan().Sort(comparison);
        AssertSameOrder(expected, data, comparison, $"comparison length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Sort_ComparisonMethodGroup_MatchesDotNetOrder(int length, bool managed)
    {
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6260 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortMethodGroupKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        var expected = (int[])input.Clone();
        expected.AsSpan().Sort(SpanSortKernels.CompareLowNibble);
        AssertSameOrder(expected, data, SpanSortKernels.CompareLowNibble, $"method group length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Sort_NullComparer_UsesDefaultOrder(bool managed)
    {
        var input = SortInputs.Int(100, SortPattern.FewDistinct, seed: 6261);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortNullComparerKernel(
            data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameAsDotNet(input, data, "null comparer");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Sort_ComparableStruct_MatchesDotNetOrder(int length, bool managed)
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
    [InlineData(64, false)]
    [InlineData(1000, false)]
    [InlineData(64, true)]
    [InlineData(1000, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Sort_HeapSortKillerInputWithEqualKeys_MatchesDotNetOrder(int length, bool managed)
    {
        var values = SortInputs.HeapSortKiller(length);
        var input = new SortRecord[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = new SortRecord { Key = values[i] / 2, Tag = i };
        }

        var data = (SortRecord[])input.Clone();
        Run(managed, () => SpanSortKernels.LaunchSortComparableKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_down_heap `compare(keys[child - 1], keys[child]) < 0` -> `<= 0`
        // (equal keys move)
        var expected = (SortRecord[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());
        AssertSameOrder(expected, data, static (a, b) => a.CompareTo(b), $"heap sort comparable of length {length}");
    }

    // An inconsistent comparer runs the partition scan past the partition, which .NET reports as ArgumentException
    // (BadComparer). Length 16 sorts by insertion only, which never reads past the span:
    // neither .NET nor the port throws.
    [SkippableTheory]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Sort_InconsistentComparer_ThrowsArgumentException(int length, bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6268);
        var data = (int[])input.Clone();
        var expected = (int[])input.Clone();
        var dotNetThrows = Record.Exception(
            () => MemoryExtensions.Sort(expected.AsSpan(), new AlwaysLessComparer())) is ArgumentException;
        Assert.Equal(length > 16, dotNetThrows);

        // mutant: Sort.hpp __gsharp_sort_bad_comparer traps with InvalidOperation
        // instead of Argument (wrong exception type)
        var exception = Record.Exception(() => Run(managed,
            () => SpanSortKernels.LaunchSortBogusComparerKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal(dotNetThrows, exception is ArgumentException);
        if (!dotNetThrows)
        {
            Assert.Equal(expected, data);
        }
    }

    // .NET wraps an exception thrown by the comparison in InvalidOperationException (ElementAt throws
    // ArgumentOutOfRangeException here); the native CPU path records the trap inside the comparison the same way
    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Sort_ExceptionInComparisonCallback_ThrowsInvalidOperationException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] data = [2, 99, 1, 0];
        int[] table = [10, 20, 30];

        // mutant: ErrorChannel.hpp __gsharp_trap without the translation inside a comparison
        // (ArgumentOutOfRangeException)
        var exception = Record.Exception(() => Run(managed,
            () => SpanSortKernels.LaunchSortTrappingComparisonKernel(
                data, table, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.IsType<InvalidOperationException>(exception);
    }
}
