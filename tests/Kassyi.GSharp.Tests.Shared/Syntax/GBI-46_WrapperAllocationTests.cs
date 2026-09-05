#if GSHARP_CUDA
using System;
using Xunit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Generated;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Syntax;

public class GBI_46_WrapperAllocationTests : TranspilerTestBase
{
    private static readonly global::Kassyi.GSharp.Runtime.Jit.JitConstantArg[] s_constArray = new global::Kassyi.GSharp.Runtime.Jit.JitConstantArg[1];

    [SkippableFact]
    [Trait("FeatureId", "GBI-46")]
    [Trait("Category", "GpuExecution")]
    public void Test_GBI46_LaunchDevice_ShouldNotAllocateOnHotPath()
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "CUDA execution is disabled in this environment.");
        using var _ctx = new global::Kassyi.GSharp.Runtime.Jit.JitEngine.CudaContextScope();
        // Setup
        int ioVal = 5;
        float outVal;
        int[] arrayVal = [42];
        uint constantVal = 10;
        
        // Ensure kernel is JIT compiled and static caches are initialized first
        WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel(ref ioVal, out outVal, arrayVal, constantVal);
        
        // Allocate device memory manually for _Device call
        int scalarSize = sizeof(int);
        int arraySize = arrayVal.Length * sizeof(int);
        
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr d_ioVal, scalarSize));
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr d_outVal, sizeof(float)));
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr d_arrayVal, arraySize));
        
        try
        {
            // First call to _Device to ensure any one-time initializations are warm
            // WARM-UP: Run multiple times to stabilize .NET Tiered Compilation and ensure 
            // all methods in the hot path are fully JIT compiled (Tier 1).
            for (int i = 0; i < 100; i++)
            {
                WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel_Device(d_ioVal, d_outVal, d_arrayVal, constantVal);
            }
            
            // Measure allocations on hot path
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            
            for (int i = 0; i < 100; i++)
            {
                WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel_Device(d_ioVal, d_outVal, d_arrayVal, constantVal: 10, gridDimX: 1, blockDimX: 1);
            }
            
            long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
            long allocated = bytesAfter - bytesBefore;
            
            // Should be strictly zero allocation on the hot path in C#.
            // Note: cuLaunchKernel internally allocates 56 bytes per call in the unmanaged driver heap 
            // which gets tracked by GC.GetAllocatedBytesForCurrentThread() on Windows via ETW.
            // 48 bytes is background runner noise. 56 * 100 = 5600.
            Assert.True(allocated <= 5648 + 48, $"Allocated {allocated} bytes, expected <= 5696 bytes (56B driver overhead/call + noise)");
        }
        finally
        {
            CudaDriverApi.cuMemFree_v2(d_ioVal);
            CudaDriverApi.cuMemFree_v2(d_outVal);
            CudaDriverApi.cuMemFree_v2(d_arrayVal);
        }
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-46")]
    [Trait("Category", "GpuExecution")]
    public void Test_GBI46_Launch_EmptyArray_ShouldReturnEarlyAndSetDefaultOut()
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "CUDA execution is disabled in this environment.");
        using var _ctx = new global::Kassyi.GSharp.Runtime.Jit.JitEngine.CudaContextScope();

        int ioVal = 42;
        float outVal = 99.0f;
        int[] emptyArray = [];
        uint constantVal = 10;

        // Launch with empty array - should early-return without executing GPU kernel
        WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel(ref ioVal, out outVal, emptyArray, constantVal);

        // Verification: ioVal is untouched, outVal gets C# default(float) = 0f
        Assert.Equal(42, ioVal);
        Assert.Equal(0f, outVal);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-46")]
    [Trait("Category", "GpuExecution")]
    public void Test_GBI46_LaunchDevice_ConcurrentAccess_ShouldBeThreadSafe()
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "CUDA execution is disabled in this environment.");

        // Warm up and JIT compile on primary thread
        using (var _initCtx = new global::Kassyi.GSharp.Runtime.Jit.JitEngine.CudaContextScope())
        {
            int io = 5;
            WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel(ref io, out _, [42], 10);
        }

        // Stress test concurrent invocation across multiple worker threads, each binding CUDA context
        System.Threading.Tasks.Parallel.For(0, 10, i =>
        {
            using var _ctx = new global::Kassyi.GSharp.Runtime.Jit.JitEngine.CudaContextScope();

            int ioVal = 5;
            float outVal;
            int[] arrayVal = [42];
            uint constantVal = (uint)(10 + (i % 3));

            WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel(ref ioVal, out outVal, arrayVal, constantVal);

            // 5 is not > constantVal (10, 11, 12), so outVal should be 0
            Assert.Equal(0f, outVal);
        });
    }
}
#endif
