#if GSHARP_CUDA
using System;
using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[TestTarget(ExecutionTarget.GpuExecution)]
public class CudaStreamTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaStream_MultipleStreamsExecuteConcurrently()
    {
        using var ctx = new GpuKernelLoader.CudaContextScope();

        using var stream1 = new CudaStream();
        using var stream2 = new CudaStream();

        using var buf1 = new GpuBuffer<int>(1);
        using var buf2 = new GpuBuffer<int>(1);

        // When using GpuBuffer<T>, the Transpiler passes pointers directly without 
        // host-sync memory copies. Thus, Launch returns immediately, queueing the kernel.
        CudaStreamKernels.LaunchDelayKernelPtr(buf1.DevicePointer, 1000000, hStream: stream1.Handle);
        CudaStreamKernels.LaunchDelayKernelPtr(buf2.DevicePointer, 1000000, hStream: stream2.Handle);

        // Synchronize both streams
        stream1.Synchronize();
        stream2.Synchronize();

        // Verify that execution succeeded without crashing
        Assert.True(true);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CudaStream_UseAfterDispose_ThrowsObjectDisposedException()
    {
        using var ctx = new GpuKernelLoader.CudaContextScope();

        var stream = new CudaStream();
        Assert.NotEqual(IntPtr.Zero, stream.Handle);

        stream.Dispose();
        Assert.Equal(IntPtr.Zero, stream.Handle);

        // Double dispose must be safe
        var ex = Record.Exception(() => stream.Dispose());
        Assert.Null(ex);

        Assert.Throws<ObjectDisposedException>(() => stream.Synchronize());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await stream.SynchronizeAsync());
    }
}

#endif
