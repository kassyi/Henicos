using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>Span&lt;T&gt;</c> slice mutation, fill, and reverse (#731).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanSliceMutationTests : TranspilerTestBase
{
    public static TheoryData<bool> Paths => SpanWriteTestHelpers.Paths;

    public static TheoryData<int, int, bool> SliceWindowCases() => SpanWriteTestHelpers.SliceWindowCases();

    private void Run(bool managed, Action launch) => SpanWriteTestHelpers.Run(IsCudaDialect, managed, launch);

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

        Run(managed, () => SpanWriteKernels.LaunchIssue731AsSpanFillReverseKernel(
            data, gridDimX: 1, blockDimX: Threads));

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

        Run(managed, () => SpanWriteKernels.LaunchFillSliceKernel(
            actual, offset, count, -42, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchClearSliceKernel(
            actual, offset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchClearStructSliceKernel(
            actual, offset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchStackAllocFillClearReverseKernel(
            actual, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        // mutant: Types.hpp GSharpArray<T>::Reverse loop bound `Length / 2 - 1`
        // (middle pair of even-length span stays unswapped)
        Run(managed, () => SpanWriteKernels.LaunchReverseSliceKernel(
            actual, offset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchReverseVariantsKernel(
            a, b, c, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([6, 5, 4, 3, 2, 1], a);
        Assert.Equal([50, 40, 30, 20, 10], b);
        Assert.Equal([50, 40, 30, 20, 10], c);
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

        Run(managed, () => SpanWriteKernels.LaunchAsSpanOverloadsKernel(
            data, 2, 3, results, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchSpanEmptyAndIsEmptyKernel(
            data, results, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal([1, 1, 1, 1], results);
        Assert.Equal([7, 8, 9], data);
    }
}
