using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Runtime;

public class GEN153_GpuBufferTests
{
    [SkippableTheory]
    [Trait("FeatureId", "GEN-153")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    public void Test_GEN153_GpuBuffer_HappyPath_RoundTrip(int count)
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        int[] sourceData = new int[count];
        for (int i = 0; i < count; i++)
        {
            sourceData[i] = i * 42;
        }

        using var buffer = new GpuBuffer<int>(count);
        Assert.Equal(count, buffer.Length);

        // When Length is 0, device pointer is technically unspecified but should not crash
        if (count > 0)
        {
            Assert.NotEqual(IntPtr.Zero, buffer.DevicePointer);
        }

        buffer.CopyToDevice(sourceData);

        int[] destData = new int[count];
        buffer.CopyToHost(destData);

        Assert.Equal(sourceData, destData);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-153")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN153_GpuBuffer_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuBuffer<int>(-1));
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-153")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN153_GpuBuffer_OversizedSpan_ThrowsArgumentException()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        using var buffer = new GpuBuffer<int>(10);
        int[] oversizedData = new int[11];

        var ex1 = Assert.Throws<ArgumentException>(() => buffer.CopyToDevice(oversizedData));
        Assert.Contains("Source span is larger than the allocated device buffer", ex1.Message);

        var ex2 = Assert.Throws<ArgumentException>(() => buffer.CopyToHost(oversizedData));
        Assert.Contains("Destination span is larger than the allocated device buffer", ex2.Message);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-153")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN153_GpuBuffer_UseAfterDispose_ThrowsObjectDisposedException()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        var buffer = new GpuBuffer<int>(10);
        buffer.Dispose();

        int[] data = new int[5];

        Assert.Throws<ObjectDisposedException>(() => buffer.CopyToDevice(data));
        Assert.Throws<ObjectDisposedException>(() => buffer.CopyToHost(data));

        // Double dispose should be safe and not throw
        var exception = Record.Exception(() => buffer.Dispose());
        Assert.Null(exception);
    }
}
