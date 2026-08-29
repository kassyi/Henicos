// ReSharper disable All
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

[InlineArray(10)]
public struct TenIntInlineArray
{
    private int _element0;
}

public struct PaddedByteDoubleStruct
{
    public byte Flag1;
    public double ValueD;
    public int ValueI;
    public short Flag2;
}

public static class GEN_23_31_TypeSystemAndStructKernels
{
    // GEN-23: Multiple fixed variable declarations and restrict mapping
    [GpuKernel]
    public static unsafe void FixedRestrictKernel(int[] src, int[] dest)
    {
        fixed (int* pSrc = &src[0], pDest = &dest[0])
        {
            pDest[0] = pSrc[0] + 42;
        }
    }

    // GEN-28: Dynamic shared memory with multiple types
    [GpuKernel]
    public static void DynamicSharedMemoryMultiTypeKernel(int[] output, int count1, int count2)
    {
        SharedMemory<byte> smemBytes = new(count1);
        SharedMemory<double> smemDoubles = new(count2);

        int tid = ThreadIndex.X;
        if (tid < count1)
        {
            smemBytes[tid] = (byte)(tid + 1);
        }

        if (tid < count2)
        {
            smemDoubles[tid] = (tid + 1) * 2.5;
        }

        if (tid == 0)
        {
            output[0] = smemBytes[0] + (int)smemDoubles[0];
        }
    }

    // GEN-30: InlineArray(10) struct execution
    [GpuKernel]
    public static void InlineArrayKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        if (tid == 0)
        {
            TenIntInlineArray arr = default;
            for (int i = 0; i < 10; i++)
            {
                arr[i] = (i + 1) * 10;
            }

            int sum = 0;
            for (int i = 0; i < 10; i++)
            {
                sum += arr[i];
            }

            output[0] = sum;
        }
    }

    // Edge case: byte/double mixed struct alignment kernel
    [GpuKernel]
    public static void PaddedByteDoubleKernel(PaddedByteDoubleStruct[] items, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            PaddedByteDoubleStruct item = items[tid];
            item.Flag1 = (byte)(item.Flag1 + 1);
            item.ValueD = item.ValueD * 2.0;
            item.ValueI = item.ValueI + 10;
            item.Flag2 = (short)(item.Flag2 - 1);
            items[tid] = item;
        }
    }
}
