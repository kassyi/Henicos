#nullable enable
using System;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanWriteDiagnosticsTests
{
    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArrayClear_OutOfRangeParameters_ThrowsIndexOutOfRangeException(bool managed)
    {
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<IndexOutOfRangeException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchOutOfRangeArrayClearKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("434", exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArrayFill_OutOfRangeParameters_ThrowsArgumentOutOfRangeException(bool managed)
    {
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchOutOfRangeArrayFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.Equal([7, 7, 7, 7], a);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("445", exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArrayCopy_OutOfRangeParameters_ThrowsArgumentException(bool managed)
    {
        var src = new int[] { 1, 2, 3, 4 };
        var dst = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchOutOfRangeArrayCopyKernel(
                src, dst, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.Equal([7, 7, 7, 7], dst);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("456", exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void AsSpanFill_OutOfRangeParameters_ThrowsArgumentOutOfRangeException(bool managed)
    {
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchOutOfRangeAsSpanFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.Equal([7, 7, 7, 7], a);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("467", exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void SliceFill_OutOfRangeParameters_ThrowsArgumentOutOfRangeException(bool managed)
    {
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchOutOfRangeSliceFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.Equal([7, 7, 7, 7], a);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("478", exception.Message);
        }
    }
}
