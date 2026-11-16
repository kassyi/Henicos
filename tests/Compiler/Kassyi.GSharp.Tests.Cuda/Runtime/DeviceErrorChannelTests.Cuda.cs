using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class DeviceErrorChannelTests
{
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_CudaExecution_ThrowsStandardExceptionWithCoordinates()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchCoordinate3DTrapKernel(
                data,
                targetTx: 2,
                targetTy: 1,
                targetTz: 1,
                targetBx: 1,
                targetBy: 2,
                targetBz: 1,
                result,
                gridDimX: 3,
                gridDimY: 4,
                gridDimZ: 2,
                blockDimX: 4,
                blockDimY: 3,
                blockDimZ: 2));

        Assert.Contains("Index was out of range.", ex.Message);
        Assert.Contains("in thread (2,1,1) of block (1,2,1)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(2,1,1)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(1,2,1)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_ConcurrentTrapOnCuda_FirstWriterWinsWithoutRace()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [1, 2, 3, 4];
        int[] result = new int[256];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchConcurrentTrapAllThreadsKernel(
                data, result, gridDimX: 4, blockDimX: 64));

        Assert.Contains("Sequence contains no matching element", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.NotNull(ex.Data["GSharp.ThreadIndex"]);
        Assert.NotNull(ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_CudaExecution_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 999, result, blockDimX: 1));

        Assert.Contains("Index was out of range.", ex.Message);
        Assert.Contains("in StackLinqElementAtKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqElementAtKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_GridOneHundredBlocksOnCuda_PreservesBlockCoordinate()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchBlockIndex100TrapKernel(
                data,
                targetBlock: 37,
                result,
                gridDimX: 100,
                blockDimX: 32
            )
        );

        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(37,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Contains("of block (37,0,0)", ex.Message);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqFirstOnEmptySequenceOnCuda_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [1, 2, 3];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqFirstEmptyKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains no elements", ex.Message);
        Assert.Contains("in StackLinqFirstEmptyKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqFirstEmptyKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqSingleOnMultipleElementsOnCuda_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqSingleMultipleKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains more than one element", ex.Message);
        Assert.Contains("in StackLinqSingleMultipleKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqSingleMultipleKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqMaxOnEmptySequenceOnCuda_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [1, 2, 3];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqMaxEmptyKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains no elements", ex.Message);
        Assert.Contains("in StackLinqMaxEmptyKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqMaxEmptyKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void SwitchExpression_MatchedArmsOnCuda_ReturnsArmValues()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] keys = [1, 0];
        int[] result = new int[keys.Length];

        DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2);

        Assert.Equal([20, 10], result);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmOnCuda_CarriesSourceLocation()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] keys = [1, 2];
        int[] result = new int[keys.Length];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2));

        Assert.Contains("Non-exhaustive switch expression failed to match its input.", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("SwitchExpressionNoMatchKernel", ex.Data["GSharp.MemberName"]);
        Assert.Equal("DeviceErrorChannelKernels.cu.cs(97,21)", ex.Data["GSharp.SourceLocation"]);
        Assert.Equal("(1,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmInDeviceFunctionOnCuda_ThrowsSwitchExpressionException()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] keys = [2];
        int[] result = [0];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchInDeviceFunctionKernel(
                keys,
                result,
                blockDimX: 1
            )
        );

        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Null(ex.InnerException);
    }
}
