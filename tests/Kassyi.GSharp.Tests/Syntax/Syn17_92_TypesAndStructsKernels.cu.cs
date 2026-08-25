using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct Vector2
{
    public float X;
    public float Y;
}

public struct MixedPaddingStruct
{
    public byte Flag1;
    public double ValueD;
    public int ValueI;
    public short Flag2;
}

public static class Syn17_92_TypesAndStructsKernels
{
    [GpuKernel]
    public static void StructKernel(Vector2[] vectors, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            Vector2 v = vectors[tid];
            v.X = v.X * 2.0f;
            v.Y = v.Y + 1.0f;
            vectors[tid] = v;
        }
    }

    [GpuKernel]
    public static void PaddedStructKernel(MixedPaddingStruct[] items, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            MixedPaddingStruct item = items[tid];
            item.Flag1 = (byte)(item.Flag1 + 1);
            item.ValueD = item.ValueD * 1.5;
            item.ValueI = item.ValueI + 100;
            item.Flag2 = (short)(item.Flag2 - 5);
            items[tid] = item;
        }
    }
}
