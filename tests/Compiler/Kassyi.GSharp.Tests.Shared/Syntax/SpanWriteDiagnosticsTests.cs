using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>Span&lt;T&gt;</c> and <c>Array</c> out-of-range diagnostics
/// and evaluation order (#731).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpanWriteDiagnosticsTests : TranspilerTestBase
{
    public static TheoryData<bool> Paths => SpanWriteTestHelpers.Paths;

    private void Run(bool managed, Action launch) => SpanWriteTestHelpers.Run(IsCudaDialect, managed, launch);

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_ArrayClear_ThrowsIndexOutOfRangeException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<IndexOutOfRangeException>(() => Run(managed,
            () => SpanWriteKernels.LaunchOutOfRangeArrayClearKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)));
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("366", exception.Message);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_ArrayFill_ThrowsArgumentOutOfRangeException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(managed,
            () => SpanWriteKernels.LaunchOutOfRangeArrayFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal([7, 7, 7, 7], a);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("377", exception.Message);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_ArrayCopy_ThrowsArgumentException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var src = new int[] { 1, 2, 3, 4 };
        var dst = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentException>(() => Run(managed,
            () => SpanWriteKernels.LaunchOutOfRangeArrayCopyKernel(
                src, dst, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal([7, 7, 7, 7], dst);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("388", exception.Message);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_AsSpanFill_ThrowsArgumentOutOfRangeException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(managed,
            () => SpanWriteKernels.LaunchOutOfRangeAsSpanFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal([7, 7, 7, 7], a);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("399", exception.Message);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_SliceFill_ThrowsArgumentOutOfRangeException(bool managed)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap loses its code and spoils the context; the exception is checked on the CPU paths.");
        var a = new int[] { 7, 7, 7, 7 };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(managed,
            () => SpanWriteKernels.LaunchOutOfRangeSliceFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX)));
        Assert.Equal([7, 7, 7, 7], a);
        if (!managed)
        {
            Assert.Contains("SpanWriteKernels.cu.cs", exception.Message);
            Assert.Contains("410", exception.Message);
        }
    }

#if GSHARP_CUDA
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_ArrayFill_CudaTraps()
    {
        Skip.If(true,
            "CUDA checks out-of-range slice and span members only when built with " +
            "GSharpCheckedBuild (-DGSHARP_CHECKED); unskip once #716 lands.");
        var a = new int[] { 7, 7, 7, 7 };
        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            SpanWriteKernels.LaunchOutOfRangeArrayFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_ArrayCopy_CudaTraps()
    {
        Skip.If(true,
            "CUDA checks out-of-range slice and span members only when built with " +
            "GSharpCheckedBuild (-DGSHARP_CHECKED); unskip once #716 lands.");
        var src = new int[] { 7, 7, 7, 7 };
        var dst = new int[] { 7, 7, 7, 7 };
        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            SpanWriteKernels.LaunchOutOfRangeArrayCopyKernel(src, dst, gridDimX: GridDimX, blockDimX: BlockDimX));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_AsSpanFill_CudaTraps()
    {
        Skip.If(true,
            "CUDA checks out-of-range slice and span members only when built with " +
            "GSharpCheckedBuild (-DGSHARP_CHECKED); unskip once #716 lands.");
        var a = new int[] { 7, 7, 7, 7 };
        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            SpanWriteKernels.LaunchOutOfRangeAsSpanFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OutOfRange_SliceFill_CudaTraps()
    {
        Skip.If(true,
            "CUDA checks out-of-range slice and span members only when built with " +
            "GSharpCheckedBuild (-DGSHARP_CHECKED); unskip once #716 lands.");
        var a = new int[] { 7, 7, 7, 7 };
        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            SpanWriteKernels.LaunchOutOfRangeSliceFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX));
    }
#endif

    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EvaluationOrder_LeftToRightAndSingleEvaluation(bool managed)
    {
        var src = CreateSequential(8, start: 10);
        var dst1 = new int[4];
        var dst2 = new int[6];
        var dst3 = new int[6];
        var counters = new int[4];

        var expectedDst1 = new int[4];
        var expectedDst2 = new int[6];
        var expectedDst3 = new int[6];
        var expectedCounters = new int[4];
        {
            var i = 1;
            src.AsSpan().Slice(i++, i++).CopyTo(expectedDst1.AsSpan());
            expectedCounters[0] = i;

            var j = 1;
            Array.Fill(expectedDst2, j * 10, j++, ++j);
            expectedCounters[1] = j;

            var k = 0;
            var len = 2;
            Array.Copy(src, k++, expectedDst3, k++, ++len);
            expectedCounters[2] = k;
            expectedCounters[3] = len;
        }

        Run(managed, () => SpanWriteKernels.LaunchEvaluationOrderSpanWriteKernel(
            src, dst1, dst2, dst3, counters, gridDimX: GridDimX, blockDimX: BlockDimX));

        Assert.Equal(expectedCounters, counters);
        Assert.Equal(expectedDst1, dst1);
        Assert.Equal(expectedDst2, dst2);
        Assert.Equal(expectedDst3, dst3);
    }
}
