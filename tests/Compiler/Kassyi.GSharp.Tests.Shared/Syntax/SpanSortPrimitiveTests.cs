using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanSortTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>MemoryExtensions.Sort</c> for primitive types in device code (#625).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanSortPrimitiveTests : SpanKernelPathTestBase
{
    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.IntCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SortInt_VaryingLengthsAndPatterns_MatchesDotNetOrder(int length, SortPattern pattern, bool managed)
    {
        var input = SortInputs.Int(length, pattern, seed: 625 + length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_insertion `j >= 0` -> `j > 0`
        // (the first element never moves; descending, length 4)
        AssertSameAsDotNet(input, data, $"int length {length} {pattern}");
    }

    // The input that McIlroy's adversary builds against the .NET introsort exhausts the depth limit,
    // so the heap sort runs
    [SkippableTheory]
    [InlineData(64, false)]
    [InlineData(1000, false)]
    [InlineData(64, true)]
    [InlineData(1000, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void SortInt_DepthLimitExhaustionInput_UsesHeapSortAndMatchesDotNetOrder(int length, bool managed)
    {
        var input = SortInputs.HeapSortKiller(length);
        var data = (int[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortIntKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_down_heap `child < n` -> `child <= n` (reads past the heap; order breaks)
        AssertSameAsDotNet(input, data, $"heap sort input of length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SortLong_RandomInt64Values_MatchesDotNetOrder(int length, bool managed)
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
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SortUInt_UnsignedComparisonsAboveIntMax_MatchesDotNetOrder(int length, bool managed)
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
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SortChar_RandomUtf16Characters_MatchesDotNetOrder(int length, bool managed)
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
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SortEnum_EnumValues_OrdersByUnderlyingValue(int length, bool managed)
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
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void SortFloat_FloatingPointValuesWithNaNAndSignedZeros_PlacesNaNFirstAndOrdersSignedZeros(
        int length,
        bool managed)
    {
        var input = SortInputs.Single(length, seed: 6255 + length);
        var data = (float[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortFloatKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: Sort.hpp __gsharp_sort_default without __gsharp_sort_move_nans_to_front (NaN is not first)
        AssertSameBits(input, data, BitConverter.SingleToInt32Bits, $"float length {length}");
    }

    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.TypedCases), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void SortDouble_DoublePrecisionValuesWithNaNAndSignedZeros_PlacesNaNFirstAndOrdersSignedZeros(
        int length,
        bool managed)
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
    public void SortFloat_InfinitiesAndMultipleNaNs_SortsInfinitiesAndNaNsCorrectly(float[] input, bool managed)
    {
        var data = (float[])input.Clone();

        Run(managed, () => SpanSortKernels.LaunchSortFloatKernel(data, gridDimX: GridDimX, blockDimX: BlockDimX));

        AssertSameBits(input, data, BitConverter.SingleToInt32Bits, "special values");
    }

    // Each of the 64 threads sorts a different segment at the same time
    [SkippableTheory]
    [MemberData(nameof(SpanSortTestHelpers.Paths), MemberType = typeof(SpanSortTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SortSegments_ConcurrentThreadSegments_SortsEachThreadSegmentIndependently(bool managed)
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
}
