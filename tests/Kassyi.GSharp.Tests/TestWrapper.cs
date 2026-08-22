using System;
using Kassyi.GSharp.GpuBigInt;

public class TestWrapper
{
    public void Test()
    {
        GpuBigIntKernelsWrapper.Launch_ChunkAddKernel_Device(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0);
    }
}
