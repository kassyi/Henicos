namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

using Contexts;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH1002 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntKernels
{
    [GpuKernel]
    public static void ChunkAddKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords,
        int chunkSize)
    {
        var numChunks = (numWords + chunkSize - 1) / chunkSize;

        Gpu.GridStrideLoop(numChunks, chunkIdx =>
        {
            var start = chunkIdx * chunkSize;
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
        });
    }

    [GpuKernel]
    public static void ApplyAddCarryKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords,
        int chunkSize)
    {
        Gpu.GridStrideLoop(numChunks, i =>
        {
            var cIdx = i + 1;
            var carry = carryBuffer[cIdx];
            if (carry > 0)
            {
                AtomicAddWithCarry(dResult, cIdx * chunkSize, (uint)carry, numWords);
            }
        });
    }

    [GpuDeviceFunction]
    public static void AtomicAddWithCarry(uint[] address, int wordIndex, uint valueToAdd, int maxWordCount)
    {
        var carry = valueToAdd;
        var index = wordIndex;
        while (carry > 0 && index < maxWordCount)
        {
            var oldValue = Gpu.AtomicAdd(ref address[index], carry);
            if (oldValue + carry < oldValue)
            {
                carry = 1;
                index++;
            }
            else
            {
                break;
            }
        }
    }

    [GpuKernel]
    public static void ChunkSubKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords,
        int chunkSize)
    {
        var numChunks = (numWords + chunkSize - 1) / chunkSize;

        Gpu.GridStrideLoop(numChunks, chunkIdx =>
        {
            var start = chunkIdx * chunkSize;
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
        });
    }

    [GpuKernel]
    public static void ApplySubBorrowKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords,
        int chunkSize)
    {
        Gpu.GridStrideLoop(numChunks, i =>
        {
            var cIdx = i + 1;
            var borrow = carryBuffer[cIdx];
            if (borrow > 0)
            {
                AtomicSubWithBorrow(dResult, cIdx * chunkSize, (uint)borrow, numWords);
            }
        });
    }

    [GpuDeviceFunction]
    public static void AtomicSubWithBorrow(uint[] address, int wordIndex, uint valueToSubtract, int maxWordCount)
    {
        var borrow = valueToSubtract;
        var index = wordIndex;
        while (borrow > 0 && index < maxWordCount)
        {
            var oldValue = Gpu.AtomicSub(ref address[index], borrow);
            if (oldValue < borrow)
            {
                borrow = 1;
                index++;
            }
            else
            {
                break;
            }
        }
    }

    [GpuKernel]
    public static void ShiftLeftKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
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
        });
    }

    [GpuKernel]
    public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
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
        });
    }

    [GpuKernel]
    public static void BitwiseAndKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = dA[i] & dB[i];
        });
    }

    [GpuKernel]
    public static void BitwiseOrKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = dA[i] | dB[i];
        });
    }

    [GpuKernel]
    public static void BitwiseXorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = dA[i] ^ dB[i];
        });
    }

    [GpuKernel]
    public static void BitwiseNotKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = ~dA[i];
        });
    }

    #region MacroTranspiler & Device Helpers

    [GpuDeviceFunction]
    public static void Device_ShiftLeft(BigIntKernelContext ctx, int wordShift, int bitShift)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;
            uint w0 = 0;
            uint w1 = 0;

            if (srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords)
            {
                w0 = ctx.Operands.LeftOperand[srcIdx0];
            }

            if (srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords)
            {
                w1 = ctx.Operands.LeftOperand[srcIdx1];
            }

            if (bitShift == 0)
            {
                ctx.Operands.ResultBuffer[i] = w0;
            }
            else
            {
                ctx.Operands.ResultBuffer[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
            }
        });
    }

    [GpuDeviceFunction]
    public static void Device_ShiftRight(BigIntKernelContext ctx, int wordShift, int bitShift)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;
            uint w0 = 0;
            uint w1 = 0;

            if (srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords)
            {
                w0 = ctx.Operands.LeftOperand[srcIdx0];
            }

            if (srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords)
            {
                w1 = ctx.Operands.LeftOperand[srcIdx1];
            }

            if (bitShift == 0)
            {
                ctx.Operands.ResultBuffer[i] = w0;
            }
            else
            {
                ctx.Operands.ResultBuffer[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
            }
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseAnd(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = context.Operands.LeftOperand[i] & context.Operands.RightOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseOr(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = context.Operands.LeftOperand[i] | context.Operands.RightOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseXor(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = context.Operands.LeftOperand[i] ^ context.Operands.RightOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseNot(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = ~context.Operands.LeftOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_Add(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        ulong carry = 0;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = Gpu.AddWithCarry(left[i], right[i], ref carry);
        }
    }

    [GpuDeviceFunction]
    public static void Device_Sub(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        long borrow = 0;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = Gpu.SubWithBorrow(left[i], right[i], ref borrow);
        }
    }

    [GpuDeviceFunction]
    public static void Device_ShiftLeft(ref BigIntKernelContext ctx, uint[] result, uint[] left, int shiftBits)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;
            uint w0 = 0;
            uint w1 = 0;

            if (srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords)
            {
                w0 = left[srcIdx0];
            }

            if (srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords)
            {
                w1 = left[srcIdx1];
            }

            if (bitShift == 0)
            {
                result[i] = w0;
            }
            else
            {
                result[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
            }
        }
    }

    [GpuDeviceFunction]
    public static void Device_ShiftRight(ref BigIntKernelContext ctx, uint[] result, uint[] left, int shiftBits)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;
            uint w0 = 0;
            uint w1 = 0;

            if (srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords)
            {
                w0 = left[srcIdx0];
            }

            if (srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords)
            {
                w1 = left[srcIdx1];
            }

            if (bitShift == 0)
            {
                result[i] = w0;
            }
            else
            {
                result[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
            }
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseAnd(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] & right[i];
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseOr(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] | right[i];
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseXor(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] ^ right[i];
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseNot(ref BigIntKernelContext ctx, uint[] result, uint[] operand)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = ~operand[i];
        }
    }

    #endregion
}
