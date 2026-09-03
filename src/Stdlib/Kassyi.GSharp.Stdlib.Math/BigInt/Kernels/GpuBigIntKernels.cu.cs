namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;


[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("GSharp", "GSH1002", Justification = "Lambda capture limitations in source generation prevent passing context by reference.")]
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
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

    [GpuDeviceFunction]
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

    [GpuDeviceFunction]
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
            AtomicAddWithCarry(ctx.Operands.ResultBuffer, cIdx * chunkSize, (uint)carry, ctx.NumberOfWords);
        }
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
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult, CarryBuffer = carryBuffer },
            NumberOfWords = numWords
        };
        Device_ChunkSub(ref ctx, chunkSize);
    }

    [GpuDeviceFunction]
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

    [GpuDeviceFunction]
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
            AtomicSubWithBorrow(ctx.Operands.ResultBuffer, cIdx * chunkSize, (uint)borrow, ctx.NumberOfWords);
        }
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
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_ShiftLeft(ctx, wordShift, bitShift);
    }

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

    [GpuKernel]
    public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_ShiftRight(ctx, wordShift, bitShift);
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

    [GpuKernel]
    public static void BitwiseAndKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseAnd(ctx);
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseAnd(BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            ctx.Operands.ResultBuffer[i] = ctx.Operands.LeftOperand[i] & ctx.Operands.RightOperand[i];
        });
    }

    [GpuKernel]
    public static void BitwiseOrKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseOr(ctx);
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseOr(BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            ctx.Operands.ResultBuffer[i] = ctx.Operands.LeftOperand[i] | ctx.Operands.RightOperand[i];
        });
    }

    [GpuKernel]
    public static void BitwiseXorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseXor(ctx);
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseXor(BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            ctx.Operands.ResultBuffer[i] = ctx.Operands.LeftOperand[i] ^ ctx.Operands.RightOperand[i];
        });
    }

    [GpuKernel]
    public static void BitwiseNotKernel(uint[] dA, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_BitwiseNot(ctx);
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseNot(BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            ctx.Operands.ResultBuffer[i] = ~ctx.Operands.LeftOperand[i];
        });
    }
}

