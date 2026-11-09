using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>System.Array</c> Fill, Clear, Reverse, Copy, and Sort (#731).
/// </summary>
#if GSHARP_CUDA
// A device error goes to the one error slot of the process, which a test running beside this one could read first
[Collection(TestCollections.DeviceExclusive)]
#else
[Collection(TestCollections.GlobalStateSequential)]
#endif
public class SpanArrayMethodTests : TranspilerTestBase
{
    public static TheoryData<bool> Paths => SpanWriteTestHelpers.Paths;

    public static TheoryData<int, int, int, bool> OverlapCopyCases() => SpanWriteTestHelpers.OverlapCopyCases();

    private void Run(bool managed, Action launch) => SpanWriteTestHelpers.Run(IsCudaDialect, managed, launch);

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

        Run(managed, () => SpanWriteKernels.LaunchArrayFillAndClearKernel(
            a, b, c, d, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchArrayReverseKernel(
            a, b, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchArrayCopy3Kernel(
            src, dst3, 5, gridDimX: GridDimX, blockDimX: BlockDimX));
        Run(managed, () => SpanWriteKernels.LaunchArrayCopy5Kernel(
            src, 2, dst5, 3, 4, gridDimX: GridDimX, blockDimX: BlockDimX));

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

        Run(managed, () => SpanWriteKernels.LaunchArrayCopySameBufferKernel(
            actual, srcIdx, dstIdx, length, gridDimX: GridDimX, blockDimX: BlockDimX));

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

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_ArraySortPairs_LengthMismatch_ThrowsArgumentException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanWriteKernels.LaunchArraySortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
    }
}
