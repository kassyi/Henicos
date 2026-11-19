using System;
using Kassyi.GSharp.Tests.Shared.Differential;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>System.Array</c> Fill, Clear, Reverse, Copy, and Sort (#731).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public partial class SpanArrayMethodTests : SpanKernelPathTestBase
{
    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void ArrayFillAndClear_StandardBuffers_MatchesDotNetBehavior(bool managed)
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

        Run(managed, () => SpanWriteKernels.LaunchArrayFillAndClearKernel(
            a, b, c, d, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedA, a);
        Assert.Equal(expectedB, b);
        Assert.Equal(expectedC, c);
        Assert.Equal(expectedD, d);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void ArrayReverse_FullAndSubrange_MatchesDotNetBehavior(bool managed)
    {
        var a = CreateSequential(7, start: 1);
        var b = CreateSequential(8, start: 10);
        var expectedA = (int[])a.Clone();
        var expectedB = (int[])b.Clone();
        Array.Reverse(expectedA);
        Array.Reverse(expectedB, 2, 5);

        Run(managed, () => SpanWriteKernels.LaunchArrayReverseKernel(
            a, b, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedA, a);
        Assert.Equal(expectedB, b);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void ArrayCopy_ThreeAndFiveArgumentOverloads_MatchesDotNetBehavior(bool managed)
    {
        var src = CreateSequential(8, start: 10);
        var dst3 = CreateSequential(8, start: 100);
        var dst5 = CreateSequential(8, start: 200);
        var expected3 = (int[])dst3.Clone();
        var expected5 = (int[])dst5.Clone();
        Array.Copy(src, expected3, 5);
        Array.Copy(src, 2, expected5, 3, 4);

        Run(managed, () => SpanWriteKernels.LaunchArrayCopy3Kernel(
            src, dst3, 5, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArrayCopy5Kernel(
            src, 2, dst5, 3, 4, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected3, dst3);
        Assert.Equal(expected5, dst5);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.OverlapCopyCases), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayCopy_OverlappingSameBuffer_HandlesOverlapLikeDotNet(
        int srcIdx,
        int dstIdx,
        int length,
        bool managed)
    {
        var actual = CreateSequential(20, start: 1);
        var expected = (int[])actual.Clone();
        Array.Copy(expected, srcIdx, expected, dstIdx, length);

        Run(managed, () => SpanWriteKernels.LaunchArrayCopySameBufferKernel(
            actual, srcIdx, dstIdx, length, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void ArraySort_KeysAndPairs_MatchesDotNetBehavior(bool managed)
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

        Run(managed, () => SpanWriteKernels.LaunchArraySortKeysKernel(
            keys1, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArraySortFloatKeysKernel(
            floatKeys, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArraySortPairsKernel(
            keys2, items2, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedKeys1, keys1);
        Assert.Equal(
            Array.ConvertAll(expectedFloatKeys, BitConverter.SingleToInt32Bits),
            Array.ConvertAll(floatKeys, BitConverter.SingleToInt32Bits));
        Assert.Equal(expectedKeys2, keys2);
        Assert.Equal(expectedItems2, items2);
    }

    // .NET throws ArgumentException when index + length passes the end of the array (also for an empty array and
    // length 0), and ArgumentOutOfRangeException only for a negative index or length (#1833 (Array.Reverse(array,
    // index, length) past the end throws ArgumentOutOfRangeException instead of ArgumentException on the native CPU
    // path)). mutant: ArrayReverse3 without the Argument code reports ArgumentOutOfRange for the first four cases
    [SkippableTheory]
    [MemberData(nameof(ReverseRangeCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArrayReverse_RangeOutsideTheArray_ThrowsTheExceptionOfDotNet(int length, int index, int count,
        bool pastEnd, bool managed)
    {
        Skip.If(IsCudaDialect && !DiffPaths.IsCudaCheckedBuild,
            "The release CUDA build does not check this range; the checked build does and keeps the context usable.");
        var a = new int[length];
        var oracle = Record.Exception(() => Array.Reverse(new int[length], index, count));
        Assert.IsType(pastEnd ? typeof(ArgumentException) : typeof(ArgumentOutOfRangeException), oracle);

        // Recorded inside Run, so that the skip of Run reaches xUnit instead of being taken for the launch's exception
        Exception? actual = null;
        Run(managed, () => actual = Record.Exception(
            () => SpanWriteKernels.LaunchArrayReverseRangeKernel(a, index, count, gridDimX: 1, blockDimX: 1)));

        Assert.Equal(oracle.GetType(), actual?.GetType());
    }

    public static TheoryData<int, int, int, bool, bool> ReverseRangeCases()
    {
        var data = new TheoryData<int, int, int, bool, bool>();
        foreach (var managed in (bool[])[false, true])
        {
            data.Add(0, 1, 0, true, managed);
            data.Add(4, 3, 2, true, managed);
            data.Add(4, 0, 5, true, managed);
            data.Add(4, 5, 0, true, managed);
            data.Add(4, -1, 2, false, managed);
            data.Add(4, 0, -1, false, managed);
        }

        return data;
    }
}
