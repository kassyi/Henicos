using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class DeviceErrorChannelTests
{
    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqViolationsOnCpuManagedAndNative_ThrowsStandardExceptions()
    {
        int[] data = [1, 2, 3, 4];
        int[] result = [0];

        // 1. Managed execution
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            Assert.Throws<InvalidOperationException>(() =>
                DeviceErrorChannelKernels.LaunchStackLinqFirstNoMatchKernel(data, 100, result, blockDimX: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 99, result, blockDimX: 1));
        }

        // 2. Native CPU execution
        var firstEx = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqFirstNoMatchKernel(data, 100, result, blockDimX: 1));
        Assert.Contains("Sequence contains no matching element", firstEx.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), firstEx.Data["GSharp.ErrorCode"]);
        Assert.Equal("(0,0,0)", firstEx.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", firstEx.Data["GSharp.BlockIndex"]);

        var elemEx = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 99, result, blockDimX: 1));
        Assert.Contains("Index was out of range.", elemEx.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), elemEx.Data["GSharp.ErrorCode"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_ConcurrentTrapAllThreadsOnCpuNative_FirstWriterWins()
    {
        int[] data = [1, 2, 3, 4];
        int[] result = new int[256];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchConcurrentTrapAllThreadsKernel(data, result, gridDimX: 4, blockDimX: 64));
        Assert.Contains("Sequence contains no matching element", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.NotNull(ex.Data["GSharp.ThreadIndex"]);
        Assert.NotNull(ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_ThreeDimensionalCoordinatesOnCpuNative_CapturedInExceptionData()
    {
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

        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(2,1,1)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(1,2,1)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_EndToEndExecutionOnCpuNative_CarriesSourceLocationAndMemberName()
    {
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

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_GridOneHundredBlocksOnCpuNative_PreservesBlockCoordinate()
    {
        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchBlockIndex100TrapKernel(data, targetBlock: 37, result, gridDimX: 100, blockDimX: 32));

        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(37,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Contains("of block (37,0,0)", ex.Message);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqFirstOnEmptySequenceOnCpuNative_CarriesSourceLocationAndMemberName()
    {
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
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqSingleOnMultipleElementsOnCpuNative_CarriesSourceLocationAndMemberName()
    {
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
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqMaxOnEmptySequenceOnCpuNative_CarriesSourceLocationAndMemberName()
    {
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
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void SwitchExpression_MatchedArmsOnCpuManagedAndNative_ReturnsArmValues()
    {
        int[] keys = [1, 0];
        int[] managed = new int[keys.Length];
        int[] native = new int[keys.Length];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, managed, blockDimX: 2);
        }

        DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, native, blockDimX: 2);

        Assert.Equal([20, 10], managed);
        Assert.Equal([20, 10], native);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmOnCpuManagedAndNative_ThrowsWithSourceLocation()
    {
        int[] keys = [1, 2];
        int[] result = new int[keys.Length];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            Assert.Throws<SwitchExpressionException>(() =>
                DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2));
        }

        // The native build throws the exception C# defines, as the managed path does
        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2));

        Assert.Contains("Non-exhaustive switch expression failed to match its input.", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("SwitchExpressionNoMatchKernel", ex.Data["GSharp.MemberName"]);
        Assert.Equal("DeviceErrorChannelKernels.cu.cs(97,21)", ex.Data["GSharp.SourceLocation"]);
        Assert.Equal("(1,0,0)", ex.Data["GSharp.ThreadIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmInDeviceFunctionOnCpuNative_ThrowsWithSourcePlace()
    {
        int[] keys = [2];
        int[] result = [0];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchInDeviceFunctionKernel(
                keys,
                result,
                blockDimX: 1
            )
        );

        Assert.Contains(
            "Non-exhaustive switch expression failed to match its input at DeviceErrorChannelKernels.cu.cs(108,44)",
            ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
    }
}
