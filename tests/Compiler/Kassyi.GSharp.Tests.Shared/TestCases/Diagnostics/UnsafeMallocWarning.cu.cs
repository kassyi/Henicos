using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public static partial class UnsafeMallocWarningKernels
{
    [GpuKernel]
    public static unsafe void MallocInKernel(int[] output)
    {
        void* block = Gpu.Unsafe.Malloc(16);
        Gpu.Unsafe.Free(block);
        output[ThreadIndex.X] = 1;
    }

    [GpuDeviceFunction]
    public static unsafe int MallocInDeviceFunction()
    {
        int* block = (int*)Gpu.Unsafe.Malloc(4);
        *block = 3;
        int value = *block;
        Gpu.Unsafe.Free(block);
        return value;
    }

#pragma warning disable GSH0029 // Verified: freed right below
    [GpuKernel]
    public static unsafe void SuppressedMalloc(int[] output)
    {
        void* block = Gpu.Unsafe.Malloc(16);
        Gpu.Unsafe.Free(block);
        output[ThreadIndex.X] = 2;
    }
#pragma warning restore GSH0029
}
