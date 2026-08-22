using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.GpuBigInt;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntKernels
{
    [GpuKernel]
    public static void ChunkAddKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords,
        int chunkSize)
    {
        var chunkIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var start = chunkIdx * chunkSize;
        if (start >= numWords)
        {
            return;
        }

        var end = start + chunkSize;
        if (end > numWords)
        {
            end = numWords;
        }

        ulong localCarry = 0;
        for (var i = start; i < end; i++)
        {
            dResult[i] = Gpu.AddWithCarry(dA[i], dB[i], ref localCarry);
        }

        carryBuffer[chunkIdx + 1] = (long)localCarry;
    }

    [GpuKernel]
    public static void ApplyAddCarryKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords,
        int chunkSize)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        ulong rippleCarry = 0;
        for (var chunkIdx = 0; chunkIdx < numChunks; chunkIdx++)
        {
            var start = chunkIdx * chunkSize;
            var end = start + chunkSize;
            if (end > numWords)
            {
                end = numWords;
            }

            rippleCarry += (ulong)carryBuffer[chunkIdx];
            if (rippleCarry == 0)
            {
                continue;
            }

            for (var i = start; i < end; i++)
            {
                dResult[i] = Gpu.AddWithCarry(dResult[i], 0, ref rippleCarry);
                if (rippleCarry == 0)
                {
                    break;
                }
            }
        }
    }

    [GpuKernel]
    public static void ChunkSubKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords,
        int chunkSize)
    {
        var chunkIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var start = chunkIdx * chunkSize;
        if (start >= numWords)
        {
            return;
        }

        var end = start + chunkSize;
        if (end > numWords)
        {
            end = numWords;
        }

        long localBorrow = 0;
        for (var i = start; i < end; i++)
        {
            dResult[i] = Gpu.SubWithBorrow(dA[i], dB[i], ref localBorrow);
        }

        carryBuffer[chunkIdx + 1] = localBorrow;
    }

    [GpuKernel]
    public static void ApplySubBorrowKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords,
        int chunkSize)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            long rippleBorrow = 0;
            for (var chunkIdx = 0; chunkIdx < numChunks; chunkIdx++)
            {
                var start = chunkIdx * chunkSize;
                var end = start + chunkSize;
                if (end > numWords)
                {
                    end = numWords;
                }

                rippleBorrow += carryBuffer[chunkIdx];
                if (rippleBorrow == 0)
                {
                    continue;
                }

                for (var i = start; i < end; i++)
                {
                    dResult[i] = Gpu.SubWithBorrow(dResult[i], 0, ref rippleBorrow);
                    if (rippleBorrow == 0)
                    {
                        break;
                    }
                }
            }
        }
    }

    [GpuKernel]
    public static void ShiftLeftKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;
            uint w0 = 0;
            uint w1 = 0;

            if (srcIdx0 >= 0 && srcIdx0 < numWords)
            {
                w0 = dA[srcIdx0];
            }

            if (srcIdx1 >= 0 && srcIdx1 < numWords)
            {
                w1 = dA[srcIdx1];
            }

            if (bitShift == 0)
            {
                dResult[i] = w0;
            }
            else
            {
                dResult[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
            }
        }
    }

    [GpuKernel]
    public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= numWords)
        {
            return;
        }

        var srcIdx0 = i + wordShift;
        var srcIdx1 = srcIdx0 + 1;
        uint w0 = 0;
        uint w1 = 0;

        if (srcIdx0 >= 0 && srcIdx0 < numWords)
        {
            w0 = dA[srcIdx0];
        }

        if (srcIdx1 >= 0 && srcIdx1 < numWords)
        {
            w1 = dA[srcIdx1];
        }

        if (bitShift == 0)
        {
            dResult[i] = w0;
        }
        else
        {
            dResult[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
        }
    }

    [GpuKernel]
    public static void BitwiseAndKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            dResult[i] = dA[i] & dB[i];
        }
    }
}
