using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>System.Array</c> Fill, Clear, Reverse, Copy, and Sort (#731).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanArrayMethodTests : SpanKernelPathTestBase
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

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArraySort_MismatchedKeysAndItemsLength_ThrowsArgumentException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanWriteKernels.LaunchArraySortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)));
    }

    // A null array passed to Array.Fill, Clear, Reverse or Copy throws ArgumentNullException naming the parameter, as
    // .NET does (Array.Fill(null, 5) names "array", Array.Copy names "sourceArray" first, then "destinationArray"),
    // before the call reads the array
    [SkippableTheory]
    [MemberData(nameof(NullArrayArgumentCases))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArrayMethod_NullArrayArgument_ThrowsArgumentNullException(
        int mode,
        bool sourceIsNull,
        string parameterName,
        bool managed)
    {
        Skip.If(IsCudaDialect,
            "The CUDA cells would read a null array if the check were missing; the CPU paths check it.");
        int[] a = sourceIsNull ? null : [1, 2];
        int[] b = sourceIsNull ? [3, 4] : null;
        var dst = new int[2];

        var exception = Assert.Throws<ArgumentNullException>(() => Run(managed,
            () => SpanWriteKernels.LaunchArrayNullArgumentKernel(a, b, mode, dst, gridDimX: 1, blockDimX: 1)));

        Assert.Contains($"(Parameter '{parameterName}')", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, dst[0]);
        if (managed)
        {
            Assert.Equal(0, dst[1]);
        }
    }

    public static TheoryData<int, bool, string, bool> NullArrayArgumentCases()
    {
        var data = new TheoryData<int, bool, string, bool>();
        foreach (var managed in (bool[])[false, true])
        {
            for (var mode = 0; mode < 6; mode++)
            {
                data.Add(mode, true, "array", managed);
            }

            data.Add(6, true, "sourceArray", managed);
            data.Add(7, true, "sourceArray", managed);
            data.Add(6, false, "destinationArray", managed);
            data.Add(7, false, "destinationArray", managed);
        }

        return data;
    }
}
