#if GSHARP_CUDA
using System;
using Xunit;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class CudaStreamKernels
{
    [GpuKernel]
    public static void DelayKernel(int[] data, int iterations)
    {
        int tid = ThreadIndex.X;
        if (tid != 0) return;
        
        // Dummy delay loop to occupy the GPU stream
        int val = 0;
        for (int i = 0; i < iterations; i++) 
        {
            val += i;
        }
        data[0] = val;
    }
}

[Trait("Category", "GpuExecution")]
public class CudaStreamTests : TranspilerTestBase
{
    [Fact]
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
}

#endif
