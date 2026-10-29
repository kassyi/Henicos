using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[TestTarget(ExecutionTarget.GpuExecution)]
public class WrapperAllocationTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LaunchAllocationTestKernel_PassesConstantParameter()
    {
        int[] arrayVal = [42];

        int ioBelow = 5;
        WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioBelow, out float outBelow, arrayVal, constantVal: 10);
        Assert.Equal(0f, outBelow);
        Assert.Equal(5, ioBelow);

        int ioAbove = 15;
        WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioAbove, out float outAbove, arrayVal, constantVal: 10);
        Assert.Equal(42f, outAbove);
        Assert.Equal(43, ioAbove);
    }

#if GSHARP_CUDA
    [SkippableFact]
    public void Test_LaunchDevice_ShouldNotAllocateOnHotPath()
    {
        using var _ctx = new GpuKernelLoader.CudaContextScope();
        // Setup
        int ioVal = 5;
        float outVal;
        int[] arrayVal = [42];
        uint constantVal = 10;
        
        // Ensure kernel is JIT compiled and static caches are initialized first
        WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioVal, out outVal, arrayVal, constantVal);
        
        // Allocate device memory manually for _Device call
        int scalarSize = sizeof(int);
        int arraySize = arrayVal.Length * sizeof(int);
        
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr d_ioVal, scalarSize));
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr d_outVal, sizeof(float)));
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr d_arrayVal, arraySize));
        
        try
        {
            // Warm up multiple batches in a dedicated NoInlining method so both the loop body
            // and all callees complete Tier-0 -> OSR -> Tier-1 PGO promotion before measurement.
            for (int warmup = 0; warmup < 5; warmup++)
            {
                RunHotPathBatch(d_ioVal, d_outVal, d_arrayVal, constantVal);
            }

            // Measure allocations on hot path across up to 3 batches and take the minimum.
            // Any genuine per-call hot-path allocation occurs in every batch, whereas rare
            // one-shot runtime/EventSource/JIT thread telemetry only affects a single batch.
            long allocated = long.MaxValue;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                RunHotPathBatch(d_ioVal, d_outVal, d_arrayVal, constantVal: 10);
                long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
                allocated = Math.Min(allocated, bytesAfter - bytesBefore);
                if (allocated == 0)
                {
                    break;
                }
            }

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

    // The array launcher of a kernel with a single T[] argument allocates no table of shared device regions (#721):
    // the 360 bytes of a DeviceArrayRegions per launch would exceed the bound by far over 100 launches
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LaunchSingleArray_ShouldNotAllocateRegionTable()
    {
        using var _ctx = new GpuKernelLoader.CudaContextScope();
        int[] array = [0];
        WrapperAllocationKernels.LaunchSingleArrayKernel(array);

        for (int warmup = 0; warmup < 5; warmup++)
        {
            RunSingleArrayBatch(array);
        }

        long allocated = long.MaxValue;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            RunSingleArrayBatch(array);
            long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
            allocated = Math.Min(allocated, bytesAfter - bytesBefore);
            if (allocated == 0)
            {
                break;
            }
        }

        // The same bound as the Ptr launcher: 56 bytes of driver overhead per call and 48 bytes of noise
        Assert.True(allocated <= 5648 + 48, $"Allocated {allocated} bytes, expected <= 5696 bytes");
        Assert.True(array[0] >= 601, $"The kernel ran {array[0]} times, expected at least 601");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunSingleArrayBatch(int[] array)
    {
        for (int i = 0; i < 100; i++)
        {
            WrapperAllocationKernels.LaunchSingleArrayKernel(array, gridDimX: 1, blockDimX: 1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunHotPathBatch(IntPtr d_ioVal, IntPtr d_outVal, IntPtr d_arrayVal, uint constantVal)
    {
        for (int i = 0; i < 100; i++)
        {
            WrapperAllocationKernels.LaunchAllocationTestKernelPtr(d_ioVal, d_outVal, d_arrayVal, constantVal, gridDimX: 1, blockDimX: 1);
        }
    }

    [SkippableFact]
    public void Test_LaunchEmptyArray_ShouldThrowArgumentException()
    {
        using var _ctx = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope();

        int ioVal = 42;
        float outVal = 99.0f;
        int[] emptyArray = [];
        uint constantVal = 10;

        // Launch with empty array - should throw ArgumentException
        var ex = Assert.Throws<ArgumentException>(() =>
            WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioVal, out outVal, emptyArray, constantVal));
        Assert.Equal("arrayVal", ex.ParamName);
    }

    [SkippableFact]
    public void Test_LaunchDevice_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Warm up and JIT compile on primary thread
        using (var _initCtx = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
        {
            int io = 5;
            WrapperAllocationKernels.LaunchAllocationTestKernel(ref io, out _, [42], 10);
        }

        // Stress test concurrent invocation across multiple worker threads, each binding CUDA context
        System.Threading.Tasks.Parallel.For(0, 10, i =>
        {
            using var _ctx = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope();

            int ioVal = 5;
            float outVal;
            int[] arrayVal = [42];
            uint constantVal = (uint)(10 + (i % 3));

            WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioVal, out outVal, arrayVal, constantVal);

            // 5 is not > constantVal (10, 11, 12), so outVal should be 0
            Assert.Equal(0f, outVal);
        });
    }
#endif
}
