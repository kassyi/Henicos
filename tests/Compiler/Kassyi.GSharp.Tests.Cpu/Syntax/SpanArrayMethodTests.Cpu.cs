using System;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanArrayMethodTests
{
    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArraySort_MismatchedKeysAndItemsLength_ThrowsArgumentException(bool managed)
    {
        int[] keys = [3, 1, 2];
        int[] items = [30, 10];

        Assert.Throws<ArgumentException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchArraySortPairsKernel(keys, items, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
    }

    // A null array passed to Array.Fill, Clear, Reverse or Copy throws ArgumentNullException naming the parameter, as
    // .NET does (Array.Fill(null, 5) names "array", Array.Copy names "sourceArray" first, then "destinationArray"),
    // before the call reads the array
    [Theory]
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
        int[] a = sourceIsNull ? null : [1, 2];
        int[] b = sourceIsNull ? [3, 4] : null;
        var dst = new int[2];

        var exception = Assert.Throws<ArgumentNullException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchArrayNullArgumentKernel(a, b, mode, dst, gridDimX: 1, blockDimX: 1)
        ));

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
