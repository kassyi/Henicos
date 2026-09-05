#if GSHARP_CUDA
using System;
using Xunit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Generated;

namespace Kassyi.GSharp.Tests.Syntax;

public class GBI_46_WrapperAllocationTests : TranspilerTestBase
{
    [Fact]
    [Trait("FeatureId", "GBI-46")]
    [Trait("Category", "Integration")]
    public void Test_GBI46_LaunchDevice_ShouldNotAllocateOnHotPath()
    {
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
            WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel_Device(d_ioVal, d_outVal, d_arrayVal, constantVal);
            
            // Measure allocations on hot path
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            
            for (int i = 0; i < 100; i++)
            {
                WrapperAllocationKernelsWrapper.Launch_AllocationTestKernel_Device(d_ioVal, d_outVal, d_arrayVal, constantVal);
            }
            
            long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
            long allocated = bytesAfter - bytesBefore;
            
            // Should be strictly zero allocation on the hot path
            Assert.Equal(0, allocated);
        }
        finally
        {
            CudaDriverApi.cuMemFree_v2(d_ioVal);
            CudaDriverApi.cuMemFree_v2(d_outVal);
            CudaDriverApi.cuMemFree_v2(d_arrayVal);
        }
    }
}
#endif
