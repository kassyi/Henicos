using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>Span&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c> CopyTo
/// and TryCopyTo (#731).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanCopyTests : SpanKernelPathTestBase
{
    // P3 CopyToOverlap: overlapping source and destination slices on the same buffer behave as memmove
    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.OverlapCopyCases), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_P3_CopyToSameBuffer_HandlesOverlapLikeMemmove(
        int srcOffset, int dstOffset, int count, bool managed)
    {
        var input = CreateSequential(20, start: 10);
        var actual = (int[])input.Clone();
        var expected = (int[])input.Clone();
        expected.AsSpan(srcOffset, count).CopyTo(expected.AsSpan(dstOffset, count));

        // mutant: Types.hpp GSharpArray<T>::TryCopyTo always copies forward (corrupts srcOffset < dstOffset overlap)
        Run(managed, () => SpanWriteKernels.LaunchCopyToSameBufferKernel(
            actual, srcOffset, dstOffset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchCopyToStructSameBufferKernel(
            actual, srcOffset, dstOffset, count, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
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

        Run(managed, () => SpanWriteKernels.LaunchCopyToVariantsKernel(
            src, dst1, dst2, dst3, gridDimX: GridDimX, blockDimX: BlockDimX));

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
        var expectedOk = ((ReadOnlySpan<int>)src).Slice(srcOffset, srcCount)
            .TryCopyTo(expectedDst.AsSpan(dstOffset, dstCount));

        var ok = new int[1];
        // mutant: Types.hpp GSharpArray<T>::TryCopyTo `Length > destination.Length`
        // -> `Length >= destination.Length` (exact fit fails)
        Run(managed, () => SpanWriteKernels.LaunchTryCopyToSliceKernel(
            src, srcOffset, srcCount, dst, dstOffset, dstCount, ok,
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
        Run(managed, () => SpanWriteKernels.LaunchTryCopyToSameBufferKernel(
            actual, srcOffset, srcCount, dstOffset, dstCount, ok,
            gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedOk ? 1 : 0, ok[0]);
        Assert.Equal(expected, actual);
    }

    // P4 CopyToShort on CPU: throws ArgumentException and leaves destination untouched
    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_P4_CopyTo_DestinationTooShort_ThrowsArgumentExceptionAndPreservesDestination(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
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
    public void Test_P4_CopyTo_DestinationTooShort_CudaThrowsArgumentException()
    {
        Skip.IfNot(IsCudaDialect, "Checks the CUDA device error; the exception is checked on the CPU paths.");

        int[] src = [1, 2, 3, 4];
        int[] dst = [90, 91, 92];
        // The failing thread records the error and exits; the launcher throws the exception the CPU paths throw
        var ex = Assert.Throws<ArgumentException>(() =>
            SpanWriteKernels.LaunchCopyToDirectKernel(src, dst, gridDimX: GridDimX, blockDimX: BlockDimX));
        Assert.Null(ex.InnerException);
        Assert.Equal([90, 91, 92], dst);
    }
}
