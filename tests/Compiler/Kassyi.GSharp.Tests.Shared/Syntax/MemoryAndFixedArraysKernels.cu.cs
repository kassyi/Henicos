using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct CustomPoint
{
    public float X;
    public float Y;
}

public static partial class MemoryAndFixedArraysKernels
{
    [GpuKernel]
    public static void SharedMemoryStaticKernel(int[] intOutput, float[] floatOutput, CustomPoint[] pointOutput)
    {
        SharedMemory<int> intSmem = new(256);
        SharedMemory<float> floatSmem = new(256);
        SharedMemory<CustomPoint> pointSmem = new(64);

        int tid = ThreadIndex.X;

        if (tid < 256)
        {
            intSmem[tid] = tid * 3;
            floatSmem[tid] = tid * 1.5f;
        }

        if (tid < 64)
        {
            pointSmem[tid] = new CustomPoint { X = tid * 1.0f, Y = tid * 2.0f };
        }

        Gpu.SyncThreads();

        if (tid == 0)
        {
            intOutput[0] = intSmem[10];
            floatOutput[0] = floatSmem[10];
            pointOutput[0] = pointSmem[5];
        }
    }
}
