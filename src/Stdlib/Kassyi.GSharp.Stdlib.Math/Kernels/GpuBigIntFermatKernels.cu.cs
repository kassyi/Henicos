// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.Math.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("GSharp", "GSH1002", Justification = "Lambda capture limitations in source generation prevent passing context by reference.")]
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntFermatKernels
{
    [GpuDeviceFunction]
    public static void Device_Unpack16([GpuConst] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint val = 0;
            var wordIdx = i / 2;
            if (wordIdx < numWords)
            {
                var w = dSrc[wordIdx];
                if (i % 2 == 0)
                {
                    val = w & 0xFFFF;
                }
                else
                {
                    val = w >> 16;
                }
            }

            dDst1[i] = val;
            dDst2[i] = val;
        });
    }

    [GpuDeviceFunction]
    public static void Device_Transpose_32x32([GpuConst] uint[] idata, uint[] odata, int width, int height)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_mem[];");

        var gridX = (width + 31) / 32;
        var gridY = (height + 31) / 32;
        if (BlockIndex.X >= gridX * gridY)
        {
            return;
        }

        var bx = BlockIndex.X % gridX;
        var by = BlockIndex.X / gridX;

        if (ThreadIndex.X >= 256)
        {
            return;
        }

        var tx = ThreadIndex.X % 32;
        var ty = ThreadIndex.X / 32;

        var xIndex = bx * 32 + tx;
        var yIndex = by * 32 + ty;

        Gpu.ForUnroll(0, 4, iter =>
        {
            var i = iter * 8;
            if (xIndex < width && yIndex + i < height)
            {
                Gpu.InlinePtx($"s_mem[({ty} + {i}) * 33 + {tx}] = {idata[(yIndex + i) * width + xIndex]};");
            }
        });

        Gpu.SyncThreads();

        xIndex = by * 32 + tx;
        yIndex = bx * 32 + ty;

        Gpu.ForUnroll(0, 4, iter =>
        {
            var i = iter * 8;
            if (xIndex < height && yIndex + i < width)
            {
                uint val = 0;
                Gpu.InlinePtx($"{val} = s_mem[{tx} * 33 + ({ty} + {i})];");
                odata[(yIndex + i) * height + xIndex] = val;
            }
        });
    }


    [GpuDeviceFunction]
    public static void Device_MultiplySquare(ref FermatContext ctx)
    {
        Device_Unpack16(ctx.Buffers.ValueT, ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2, ctx.Ntt.TransformLength,
            ctx.Dimensions.NumberOfWords);
        Gpu.SyncGrid();

        int n2 = 1;
        while (n2 * n2 < ctx.Ntt.TransformLength)
        {
            n2 *= 2;
        }

        var n1 = ctx.Ntt.TransformLength / n2;

        var nttArgs = new NumberTheoreticTransformKernelArgs
        {
            Buffer = ctx.Ntt.Buffers.Buffer1,
            TemporaryBuffer = ctx.Ntt.Buffers.TemporaryBuffer,
            Twiddle = ctx.Ntt.TwiddleFactors.Forward1,
            M = ctx.Ntt.TransformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        };

        // Forward NTT 1
        GpuBigIntNttKernels.Device_ForwardNTT(nttArgs);

        // Forward NTT 2
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer2;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Forward2;
        nttArgs.P = GpuBigIntNttKernels.Prime2;
        nttArgs.Pm = GpuBigIntNttKernels.BarrettMagic2;
        GpuBigIntNttKernels.Device_ForwardNTT(nttArgs);

        GpuBigIntNttKernels.Device_NTT_PointwiseSquare(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2,
            ctx.Ntt.TransformLength);
        Gpu.SyncGrid();

        // Inverse NTT 1
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer1;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Inverse1;
        nttArgs.P = GpuBigIntNttKernels.Prime1;
        nttArgs.Pm = GpuBigIntNttKernels.BarrettMagic1;
        GpuBigIntNttKernels.Device_InverseNTT(nttArgs);

        // Inverse NTT 2
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer2;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Inverse2;
        nttArgs.P = GpuBigIntNttKernels.Prime2;
        nttArgs.Pm = GpuBigIntNttKernels.BarrettMagic2;
        GpuBigIntNttKernels.Device_InverseNTT(nttArgs);
        Gpu.SyncGrid();

        var actualOutNumWords = ctx.Dimensions.NumberOfWords * 2 + 16;
        Gpu.GridStrideLoop(ref ctx, actualOutNumWords, (ref FermatContext c, int i) =>
        {
            c.Buffers.ValueT[i] = 0;
        });

        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_CRT_ComputeCoefficients(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2,
            ctx.Ntt.Buffers.Buffer3, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Ntt.TransformLength);
        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_ResolveNttCarries(
            ctx.Ntt.Buffers.Buffer3, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Buffers.ValueT,
            ctx.Ntt.TransformLength, ctx.Dimensions.NumberOfWords * 2);
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    private static void Device_AddSub(BigIntKernelContext ctx, bool isSubtract)
    {
        int chunkSize = 256;
        if (isSubtract)
        {
            GpuBigIntKernels.Device_ChunkSub(ref ctx, chunkSize);
        }
        else
        {
            GpuBigIntKernels.Device_ChunkAdd(ref ctx, chunkSize);
        }

        Gpu.SyncGrid();

        var numChunks = (ctx.NumberOfWords + chunkSize - 1) / chunkSize;
        if (isSubtract)
        {
            GpuBigIntKernels.Device_ApplySubBorrow(ref ctx, numChunks, chunkSize);
        }
        else
        {
            GpuBigIntKernels.Device_ApplyAddCarry(ref ctx, numChunks, chunkSize);
        }

        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void Device_Add(BigIntKernelContext ctx)
        => Device_AddSub(ctx, false);

    [GpuDeviceFunction]
    public static void Device_Sub(BigIntKernelContext ctx)
        => Device_AddSub(ctx, true);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Device_ShiftLeftRight(BigIntKernelContext ctx, int shiftBits, bool isRight)
    {
        var dA = ctx.Operands.LeftOperand;
        var dResult = ctx.Operands.ResultBuffer;
        var numWords = ctx.NumberOfWords;
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        Gpu.GridStrideLoop(ref ctx, numWords, (ref BigIntKernelContext c, int i) =>
        {
            int srcIdx0;
            if (isRight)
            {
                srcIdx0 = i + wordShift;
            }
            else
            {
                srcIdx0 = i - wordShift;
            }

            int srcIdx1;
            if (isRight)
            {
                srcIdx1 = srcIdx0 + 1;
            }
            else
            {
                srcIdx1 = srcIdx0 - 1;
            }

            var w0 = 0U;
            if (srcIdx0 >= 0 && srcIdx0 < c.NumberOfWords)
            {
                w0 = dA[srcIdx0];
            }

            var w1 = 0U;
            if (srcIdx1 >= 0 && srcIdx1 < c.NumberOfWords)
            {
                w1 = dA[srcIdx1];
            }

            if (bitShift == 0)
            {
                dResult[i] = w0;
            }
            else
            {
                if (isRight)
                {
                    dResult[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
                }
                else
                {
                    dResult[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
                }
            }
        });

        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void Device_ShiftLeft(BigIntKernelContext ctx, int shiftBits)
    {
        Device_ShiftLeftRight(ctx, shiftBits, false);
    }

    [GpuDeviceFunction]
    public static void Device_ShiftRight(BigIntKernelContext ctx, int shiftBits)
    {
        Device_ShiftLeftRight(ctx, shiftBits, true);
    }


    [GpuDeviceFunction]
    public static void Device_Copy(BigIntKernelContext ctx)
    {
        var dSrc = ctx.Operands.LeftOperand;
        var dDst = ctx.Operands.ResultBuffer;
        var numWords = ctx.NumberOfWords;
        Gpu.GridStrideLoop(numWords, tid =>
        {
            dDst[tid] = dSrc[tid];
        });

        Gpu.SyncGrid();
    }


    [GpuDeviceFunction, GpuInline]
    public static void Device_FermatReduceStep(ref FermatContext ctx)
    {
        GpuBigIntRef valueT = ctx.Buffers.ValueT;
        GpuBigIntRef quotient = ctx.Buffers.Quotient;
        GpuBigIntRef remainder = ctx.Buffers.Remainder;
        GpuBigIntRef temp = ctx.Buffers.TemporaryBuffer;
        GpuBigIntRef remainderMask = ctx.Buffers.RemainderMask;

        quotient = valueT >> ctx.Dimensions.BitOffset2E1;
        remainder = valueT & remainderMask;

        temp = quotient << ctx.Dimensions.Exponent;
        remainder = remainder + temp;

        temp = quotient << 1;
        remainder = remainder - quotient;
        remainder = remainder - temp;

        temp = quotient << 3;
        valueT = remainder - temp;
    }

    [GpuDeviceFunction, GpuInline]
    public static void Device_MultiplySquareAndReduce(int reduceCount, ref FermatContext ctx)
    {
        Device_MultiplySquare(ref ctx);
        for (int j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(ref ctx);
        }
    }

    [GpuDeviceFunction, GpuInline]
    public static void Device_ShiftLeftAndReduce(int reduceCount, ref FermatContext ctx)
    {
        var mathCtx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext
            {
                LeftOperand = ctx.Buffers.ValueT,
                ResultBuffer = ctx.Buffers.TemporaryBuffer
            },
            NumberOfWords = ctx.Dimensions.NumberOfWordsExtended
        };
        Device_ShiftLeft(mathCtx, 1);

        mathCtx.Operands.LeftOperand = ctx.Buffers.TemporaryBuffer;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.ValueT;
        Device_Copy(mathCtx);

        for (int j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(ref ctx);
        }
    }

    [GpuKernel(Cooperative = true)]
    public static void Kernel_StaticFermatLoop(ref FermatContext ctx, [JitConstant] string gsharpExponent,
        [JitConstant] string gsharpExponentMinus4)
    {
        for (int iter = 0; iter < Gpu.JitConstant(gsharpExponent); iter++)
        {
            Device_MultiplySquare(ref ctx);
            Gpu.ForUnroll(ref ctx, 0, 3, (ref c, _) =>
            {
                Device_FermatReduceStep(ref c);
            });
            Device_ShiftLeftAndReduce(3, ref ctx);
        }

        for (int iter = 0; iter < Gpu.JitConstant(gsharpExponentMinus4); iter++)
        {
            Device_MultiplySquare(ref ctx);
            Gpu.ForUnroll(ref ctx, 0, 3, (ref c, _) =>
            {
                Device_FermatReduceStep(ref c);
            });
        }

        // bit 3 (1)
        Device_MultiplySquare(ref ctx);
        Gpu.ForUnroll(ref ctx, 0, 9, (ref c, _) =>
        {
            Device_FermatReduceStep(ref c);
        });
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 2 (0)
        Device_MultiplySquare(ref ctx);
        Gpu.ForUnroll(ref ctx, 0, 9, (ref c, _) =>
        {
            Device_FermatReduceStep(ref c);
        });

        for (int iter = 0; iter < Gpu.JitConstant(gsharpExponentMinus4); iter++)
        {
            Device_MultiplySquare(ref ctx);
            Gpu.ForUnroll(ref ctx, 0, 3, (ref c, _) =>
            {
                Device_FermatReduceStep(ref c);
            });
        }

        // bit 3 (1)
        Device_MultiplySquare(ref ctx);
        Gpu.ForUnroll(ref ctx, 0, 9, (ref c, _) =>
        {
            Device_FermatReduceStep(ref c);
        });
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 2 (0)
        Device_MultiplySquare(ref ctx);
        Gpu.ForUnroll(ref ctx, 0, 9, (ref c, _) =>
        {
            Device_FermatReduceStep(ref c);
        });

        // bit 1 (1)
        Device_MultiplySquare(ref ctx);
        Gpu.ForUnroll(ref ctx, 0, 9, (ref c, _) =>
        {
            Device_FermatReduceStep(ref c);
        });
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 0 (0)
        Device_MultiplySquare(ref ctx);
        Gpu.ForUnroll(ref ctx, 0, 9, (ref c, _) =>
        {
            Device_FermatReduceStep(ref c);
        });
    }
}

