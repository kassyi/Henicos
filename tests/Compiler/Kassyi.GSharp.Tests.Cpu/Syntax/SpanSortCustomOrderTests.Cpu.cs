#nullable enable
using System;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanSortTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanSortCustomOrderTests
{
    // An inconsistent comparer runs the partition scan past the partition, which .NET reports as ArgumentException
    // (BadComparer). Length 16 sorts by insertion only, which never reads past the span:
    // neither .NET nor the port throws.
    [Theory]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Sort_InconsistentComparer_ThrowsArgumentException(int length, bool managed)
    {
        var input = SortInputs.Int(length, SortPattern.Random, seed: 6268);
        var data = (int[])input.Clone();
        var expected = (int[])input.Clone();
        var dotNetThrows = Record.Exception(
            () => MemoryExtensions.Sort(expected.AsSpan(), new AlwaysLessComparer())) is ArgumentException;
        Assert.Equal(length > 16, dotNetThrows);

        // mutant: Sort.hpp __gsharp_sort_bad_comparer traps with InvalidOperation
        // instead of Argument (wrong exception type)
        var exception = Record.Exception(() => Run(
            managed,
            () => SpanSortKernels.LaunchSortBogusComparerKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.Equal(dotNetThrows, exception is ArgumentException);
        if (!dotNetThrows)
        {
            Assert.Equal(expected, data);
        }
    }

    // .NET wraps an exception thrown by the comparison in InvalidOperationException (ElementAt throws
    // ArgumentOutOfRangeException here); the native CPU path records the trap inside the comparison the same way
    [Theory]
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Sort_ExceptionInComparisonCallback_ThrowsInvalidOperationException(bool managed)
    {
        int[] data = [2, 99, 1, 0];
        int[] table = [10, 20, 30];

        // mutant: ErrorChannel.hpp __gsharp_trap without the translation inside a comparison
        // (ArgumentOutOfRangeException)
        var exception = Record.Exception(() => Run(
            managed,
            () => SpanSortKernels.LaunchSortTrappingComparisonKernel(
                data, table, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.IsType<InvalidOperationException>(exception);
    }
}
