using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.GpuBigInt.Kernels;

public static class GpuBigIntBitwiseKernels
{
    [GpuKernel]
    public static void BitwiseOrKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Device_BitwiseOr(dA, dB, dResult, numWords);
    }

    [GpuDevice]
    public static void Device_BitwiseOr(uint[] left, uint[] right, uint[] result, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            result[i] = left[i] | right[i];
        }
    }

    [GpuKernel]
    public static void BitwiseXorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Device_BitwiseXor(dA, dB, dResult, numWords);
    }

    [GpuDevice]
    public static void Device_BitwiseXor(uint[] left, uint[] right, uint[] result, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            result[i] = left[i] ^ right[i];
        }
    }

    [GpuKernel]
    public static void BitwiseNotKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Device_BitwiseNot(dA, dResult, numWords);
    }

    [GpuDevice]
    public static void Device_BitwiseNot(uint[] left, uint[] result, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            result[i] = ~left[i];
        }
    }
}
