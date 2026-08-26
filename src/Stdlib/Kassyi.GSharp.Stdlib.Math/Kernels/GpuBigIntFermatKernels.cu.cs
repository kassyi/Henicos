using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Stdlib.Math.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
public static class GpuBigIntFermatKernels
{
    [GpuDeviceFunction]
    public static void Device_Unpack16([GpuConst] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < n; i += GridDimension.X * BlockDimension.X)
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
        }
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

        for (int i = 0; i < 32; i += 8)
        {
            if (xIndex < width && yIndex + i < height)
            {
                Gpu.InlinePtx($"s_mem[({ty} + {i}) * 33 + {tx}] = {idata[(yIndex + i) * width + xIndex]};");
            }
        }

        Gpu.SyncThreads();

        xIndex = by * 32 + tx;
        yIndex = bx * 32 + ty;

        for (int i = 0; i < 32; i += 8)
        {
            if (xIndex >= height || yIndex + i >= width)
            {
                continue;
            }

            uint val = 0;
            Gpu.InlinePtx($"{val} = s_mem[{tx} * 33 + ({ty} + {i})];");
            odata[(yIndex + i) * height + xIndex] = val;
        }
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
        GpuBigIntNttKernels.Device_ForwardNTT(ref nttArgs);

        // Forward NTT 2
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer2;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Forward2;
        nttArgs.P = GpuBigIntNttKernels.Prime2;
        nttArgs.Pm = GpuBigIntNttKernels.BarrettMagic2;
        GpuBigIntNttKernels.Device_ForwardNTT(ref nttArgs);

        GpuBigIntNttKernels.Device_NTT_PointwiseSquare(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2,
            ctx.Ntt.TransformLength);
        Gpu.SyncGrid();

        // Inverse NTT 1
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer1;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Inverse1;
        nttArgs.P = GpuBigIntNttKernels.Prime1;
        nttArgs.Pm = GpuBigIntNttKernels.BarrettMagic1;
        GpuBigIntNttKernels.Device_InverseNTT(ref nttArgs);

        // Inverse NTT 2
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer2;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Inverse2;
        nttArgs.P = GpuBigIntNttKernels.Prime2;
        nttArgs.Pm = GpuBigIntNttKernels.BarrettMagic2;
        GpuBigIntNttKernels.Device_InverseNTT(ref nttArgs);
        Gpu.SyncGrid();

        var actualOutNumWords = ctx.Dimensions.NumberOfWords * 2 + 16;
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < actualOutNumWords; i += GridDimension.X * BlockDimension.X)
        {
            ctx.Buffers.ValueT[i] = 0;
        }

        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_CRT_ComputeCoefficients(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2,
            ctx.Ntt.Buffers.Buffer3, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Ntt.TransformLength);
        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_ResolveNttCarries_SingleThread(
            ctx.Ntt.Buffers.Buffer3, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Buffers.ValueT,
            ctx.Ntt.TransformLength, ctx.Dimensions.NumberOfWords * 2);
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    private static void Device_AddSub(ref BigIntKernelContext ctx, bool isSubtract)
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
    public static void Device_Add(ref BigIntKernelContext ctx)
        => Device_AddSub(ref ctx, false);

    [GpuDeviceFunction]
    public static void Device_Sub(ref BigIntKernelContext ctx)
        => Device_AddSub(ref ctx, true);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Device_ShiftLeftRight(ref BigIntKernelContext ctx, int shiftBits, bool isRight)
    {
        var dA = ctx.Operands.LeftOperand;
        var dResult = ctx.Operands.ResultBuffer;
        var numWords = ctx.NumberOfWords;
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
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
            if (srcIdx0 >= 0 && srcIdx0 < numWords)
            {
                w0 = dA[srcIdx0];
            }

            var w1 = 0U;
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
                if (isRight)
                {
                    dResult[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
                }
                else
                {
                    dResult[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
                }
            }
        }

        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void Device_ShiftLeft(ref BigIntKernelContext ctx, int shiftBits)
    {
        Device_ShiftLeftRight(ref ctx, shiftBits, false);
    }

    [GpuDeviceFunction]
    public static void Device_ShiftRight(ref BigIntKernelContext ctx, int shiftBits)
    {
        Device_ShiftLeftRight(ref ctx, shiftBits, true);
    }


    [GpuDeviceFunction]
    public static void Device_Copy(ref BigIntKernelContext ctx)
    {
        var dSrc = ctx.Operands.LeftOperand;
        var dDst = ctx.Operands.ResultBuffer;
        var numWords = ctx.NumberOfWords;
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var tid = i; tid < numWords; tid += GridDimension.X * BlockDimension.X)
        {
            dDst[tid] = dSrc[tid];
        }

        Gpu.SyncGrid();
    }


    [GpuDeviceFunction, GpuInline]
    public static void Device_FermatReduceStep(ref FermatContext ctx)
    {
        var mathCtx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext
            {
                LeftOperand = ctx.Buffers.ValueT,
                ResultBuffer = ctx.Buffers.Quotient,
                CarryBuffer = ctx.Buffers.CarryBuffer
            },
            NumberOfWords = ctx.Dimensions.NumberOfWordsExtended
        };
        Device_ShiftRight(ref mathCtx, ctx.Dimensions.BitOffset2E1);

        mathCtx.Operands.LeftOperand = ctx.Buffers.ValueT;
        mathCtx.Operands.RightOperand = ctx.Buffers.RemainderMask;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.Remainder;
        GpuBigIntKernels.Device_BitwiseAnd(ref mathCtx);

        mathCtx.Operands.LeftOperand = ctx.Buffers.Quotient;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.TemporaryBuffer;
        Device_ShiftLeft(ref mathCtx, ctx.Dimensions.Exponent);

        mathCtx.Operands.LeftOperand = ctx.Buffers.Remainder;
        mathCtx.Operands.RightOperand = ctx.Buffers.TemporaryBuffer;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.Remainder;
        Device_Add(ref mathCtx);

        mathCtx.Operands.LeftOperand = ctx.Buffers.Quotient;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.TemporaryBuffer;
        Device_ShiftLeft(ref mathCtx, 1);

        mathCtx.Operands.LeftOperand = ctx.Buffers.Remainder;
        mathCtx.Operands.RightOperand = ctx.Buffers.Quotient;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.Remainder;
        Device_Sub(ref mathCtx);

        mathCtx.Operands.RightOperand = ctx.Buffers.TemporaryBuffer;
        Device_Sub(ref mathCtx);

        mathCtx.Operands.LeftOperand = ctx.Buffers.Quotient;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.TemporaryBuffer;
        Device_ShiftLeft(ref mathCtx, 3);

        mathCtx.Operands.LeftOperand = ctx.Buffers.Remainder;
        mathCtx.Operands.RightOperand = ctx.Buffers.TemporaryBuffer;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.ValueT;
        Device_Sub(ref mathCtx);
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
        Device_ShiftLeft(ref mathCtx, 1);

        mathCtx.Operands.LeftOperand = ctx.Buffers.TemporaryBuffer;
        mathCtx.Operands.ResultBuffer = ctx.Buffers.ValueT;
        Device_Copy(ref mathCtx);

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
            Device_MultiplySquareAndReduce(3, ref ctx);
            Device_ShiftLeftAndReduce(3, ref ctx);
        }

        for (int iter = 0; iter < Gpu.JitConstant(gsharpExponentMinus4); iter++)
        {
            Device_MultiplySquareAndReduce(3, ref ctx);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, ref ctx);
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, ref ctx);

        for (int iter = 0; iter < Gpu.JitConstant(gsharpExponentMinus4); iter++)
        {
            Device_MultiplySquareAndReduce(3, ref ctx);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, ref ctx);
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, ref ctx);

        // bit 1 (1)
        Device_MultiplySquareAndReduce(9, ref ctx);
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 0 (0)
        Device_MultiplySquareAndReduce(9, ref ctx);
    }
}

