using System;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanWriteDiagnosticsTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void ArrayFill_OutOfRangeParametersInCuda_TrapsExecutionWithCudaException()
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
    public void ArrayCopy_OutOfRangeParametersInCuda_TrapsExecutionWithCudaException()
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
    public void AsSpanFill_OutOfRangeParametersInCuda_TrapsExecutionWithCudaException()
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
    public void SliceFill_OutOfRangeParametersInCuda_TrapsExecutionWithCudaException()
    {
        Skip.If(true,
            "CUDA checks out-of-range slice and span members only when built with " +
            "GSharpCheckedBuild (-DGSHARP_CHECKED); unskip once #716 lands.");
        var a = new int[] { 7, 7, 7, 7 };
        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            SpanWriteKernels.LaunchOutOfRangeSliceFillKernel(a, gridDimX: GridDimX, blockDimX: BlockDimX));
    }
}
