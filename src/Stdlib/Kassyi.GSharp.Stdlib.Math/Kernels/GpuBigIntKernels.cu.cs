namespace Kassyi.GSharp.Stdlib.Math.Kernels;


[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
public static class GpuBigIntKernels
{
    [GpuKernel]
    public static void ChunkAddKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords,
        int chunkSize)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult, CarryBuffer = carryBuffer },
            NumberOfWords = numWords
        };
        Device_ChunkAdd(ref ctx, chunkSize);
    }

    [GpuDevice]
    public static void Device_ChunkAdd(ref BigIntKernelContext ctx, int chunkSize)
    {
        var chunkIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var start = chunkIdx * chunkSize;
        if (start >= ctx.NumberOfWords)
        {
            return;
        }

        var end = start + chunkSize;
        if (end > ctx.NumberOfWords)
        {
            end = ctx.NumberOfWords;
        }

        ulong localCarry = 0;
        for (var i = start; i < end; i++)
        {
            ctx.Operands.ResultBuffer[i] = Gpu.AddWithCarry(ctx.Operands.LeftOperand[i], ctx.Operands.RightOperand[i], ref localCarry);
        }

        ctx.Operands.CarryBuffer[chunkIdx + 1] = (long)localCarry;
    }

    [GpuKernel]
    public static void ApplyAddCarryKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords,
        int chunkSize)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { ResultBuffer = dResult, CarryBuffer = carryBuffer },
            NumberOfWords = numWords
        };
        Device_ApplyAddCarry(ref ctx, numChunks, chunkSize);
    }

    [GpuDevice]
    public static void Device_ApplyAddCarry(ref BigIntKernelContext ctx, int numChunks, int chunkSize)
    {
        var cIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (cIdx <= 0 || cIdx > numChunks)
        {
            return;
        }

        var carry = ctx.Operands.CarryBuffer[cIdx];
        if (carry > 0)
        {
            GpuBigIntNttKernels.AtomicAddWithCarry(ctx.Operands.ResultBuffer, cIdx * chunkSize, (uint)carry, ctx.NumberOfWords);
        }
    }

    [GpuKernel]
    public static void ChunkSubKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords,
        int chunkSize)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult, CarryBuffer = carryBuffer },
            NumberOfWords = numWords
        };
        Device_ChunkSub(ref ctx, chunkSize);
    }

    [GpuDevice]
    public static void Device_ChunkSub(ref BigIntKernelContext ctx, int chunkSize)
    {
        var chunkIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var start = chunkIdx * chunkSize;
        if (start >= ctx.NumberOfWords)
        {
            return;
        }

        var end = start + chunkSize;
        if (end > ctx.NumberOfWords)
        {
            end = ctx.NumberOfWords;
        }

        long localBorrow = 0;
        for (var i = start; i < end; i++)
        {
            ctx.Operands.ResultBuffer[i] = Gpu.SubWithBorrow(ctx.Operands.LeftOperand[i], ctx.Operands.RightOperand[i], ref localBorrow);
        }

        ctx.Operands.CarryBuffer[chunkIdx + 1] = localBorrow;
    }

    [GpuKernel]
    public static void ApplySubBorrowKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords,
        int chunkSize)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { ResultBuffer = dResult, CarryBuffer = carryBuffer },
            NumberOfWords = numWords
        };
        Device_ApplySubBorrow(ref ctx, numChunks, chunkSize);
    }

    [GpuDevice]
    public static void Device_ApplySubBorrow(ref BigIntKernelContext ctx, int numChunks, int chunkSize)
    {
        var cIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (cIdx <= 0 || cIdx > numChunks)
        {
            return;
        }

        var borrow = ctx.Operands.CarryBuffer[cIdx];
        if (borrow > 0)
        {
            GpuBigIntNttKernels.AtomicSubWithBorrow(ctx.Operands.ResultBuffer, cIdx * chunkSize, (uint)borrow, ctx.NumberOfWords);
        }
    }

    [GpuKernel]
    public static void ShiftLeftKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_ShiftLeft(ref ctx, wordShift, bitShift);
    }

    [GpuDevice]
    public static void Device_ShiftLeft(ref BigIntKernelContext ctx, int wordShift, int bitShift)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < ctx.NumberOfWords)
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
        }
    }

    [GpuKernel]
    public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_ShiftRight(ref ctx, wordShift, bitShift);
    }

    [GpuDevice]
    public static void Device_ShiftRight(ref BigIntKernelContext ctx, int wordShift, int bitShift)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= ctx.NumberOfWords)
        {
            return;
        }

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
    }

    [GpuKernel]
    public static void BitwiseAndKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseAnd(ref ctx);
    }

    [GpuDevice]
    public static void Device_BitwiseAnd(ref BigIntKernelContext ctx)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < ctx.NumberOfWords)
        {
            ctx.Operands.ResultBuffer[i] = ctx.Operands.LeftOperand[i] & ctx.Operands.RightOperand[i];
        }
    }

    [GpuKernel]
    public static void BitwiseOrKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseOr(ref ctx);
    }

    [GpuDevice]
    public static void Device_BitwiseOr(ref BigIntKernelContext ctx)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < ctx.NumberOfWords)
        {
            ctx.Operands.ResultBuffer[i] = ctx.Operands.LeftOperand[i] | ctx.Operands.RightOperand[i];
        }
    }

    [GpuKernel]
    public static void BitwiseXorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseXor(ref ctx);
    }

    [GpuDevice]
    public static void Device_BitwiseXor(ref BigIntKernelContext ctx)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < ctx.NumberOfWords)
        {
            ctx.Operands.ResultBuffer[i] = ctx.Operands.LeftOperand[i] ^ ctx.Operands.RightOperand[i];
        }
    }

    [GpuKernel]
    public static void BitwiseNotKernel(uint[] dA, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseNot(ref ctx);
    }

    [GpuDevice]
    public static void Device_BitwiseNot(ref BigIntKernelContext ctx)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < ctx.NumberOfWords)
        {
            ctx.Operands.ResultBuffer[i] = ~ctx.Operands.LeftOperand[i];
        }
    }
}
