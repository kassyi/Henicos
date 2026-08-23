using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.GpuBigInt;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntFermatKernels
{
    [GpuDevice]
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

    [GpuDevice]
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

    [GpuDevice]
    public static void Device_NTT_Shared_Row(ref NttKernelArgs args)
    {
        var dData = args.Buffer;
        var dTwiddle = args.Twiddle;
        var n = args.M;
        var l = args.CurrentL;
        var p = args.P;
        var m = args.Pm;
        var applyTwiddleBefore = args.ApplyTwiddleBefore;
        var applyTwiddleAfter = args.ApplyTwiddleAfter;
        var maxBlocks = args.MaxBlocks;
        if (BlockIndex.X >= maxBlocks)
        {
            return;
        }

        var threadsNeeded = l / 2;
        if (ThreadIndex.X >= threadsNeeded)
        {
            return;
        }

        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");

        var row = BlockIndex.X;
        var tid = ThreadIndex.X;
        var offset = row * l;

        int log2L = 0;
        while ((1 << log2L) < l)
        {
            log2L++;
        }

        int revTid1 = 0;
        int revTid2 = 0;
        for (int i = 0; i < log2L; i++)
        {
            revTid1 = (revTid1 << 1) | ((tid >> i) & 1);
            revTid2 = (revTid2 << 1) | (((tid + l / 2) >> i) & 1);
        }

        var val1 = dData[offset + tid];
        var val2 = dData[offset + tid + l / 2];

        if (applyTwiddleBefore)
        {
            var k1 = (ulong)row * (ulong)tid;
            k1 %= (ulong)n;
            val1 = GpuBigIntNttKernels.ModMulBarrett(val1, dTwiddle[k1], p, m);

            var k2 = (ulong)row * (ulong)(tid + l / 2);
            k2 %= (ulong)n;
            val2 = GpuBigIntNttKernels.ModMulBarrett(val2, dTwiddle[k2], p, m);
        }

        Gpu.InlinePtx($"s_row[{revTid1}] = {val1};");
        Gpu.InlinePtx($"s_row[{revTid2}] = {val2};");
        Gpu.SyncThreads();

        for (int s = 0; s < log2L; s++)
        {
            var len = 2 << s;
            var halfLen = len / 2;
            var blockIdx = tid / halfLen;
            var idx = tid % halfLen;
            var pos = blockIdx * len + idx;

            var twiddleIdx = idx * (n / len);
            var w = dTwiddle[twiddleIdx];

            uint u = 0;
            Gpu.InlinePtx($"{u} = s_row[{pos}];");

            uint vSrc = 0;
            Gpu.InlinePtx($"{vSrc} = s_row[{pos + halfLen}];");
            var v = GpuBigIntNttKernels.ModMulBarrett(vSrc, w, p, m);

            var addRes = GpuBigIntNttKernels.ModAdd(u, v, p);
            var subRes = GpuBigIntNttKernels.ModSub(u, v, p);

            Gpu.InlinePtx($"s_row[{pos}] = {addRes};");
            Gpu.InlinePtx($"s_row[{pos + halfLen}] = {subRes};");
            Gpu.SyncThreads();
        }

        if (applyTwiddleAfter)
        {
            var k1 = (ulong)row * (ulong)tid;
            k1 %= (ulong)n;
            uint r1 = 0;
            Gpu.InlinePtx($"{r1} = s_row[{tid}];");
            r1 = GpuBigIntNttKernels.ModMulBarrett(r1, dTwiddle[k1], p, m);
            Gpu.InlinePtx($"s_row[{tid}] = {r1};");

            var k2 = (ulong)row * (ulong)(tid + l / 2);
            k2 %= (ulong)n;
            uint r2 = 0;
            Gpu.InlinePtx($"{r2} = s_row[{tid + l / 2}];");
            r2 = GpuBigIntNttKernels.ModMulBarrett(r2, dTwiddle[k2], p, m);
            Gpu.InlinePtx($"s_row[{tid + l / 2}] = {r2};");
        }

        uint out1 = 0;
        uint out2 = 0;
        Gpu.InlinePtx($"{out1} = s_row[{tid}];");
        Gpu.InlinePtx($"{out2} = s_row[{tid + l / 2}];");

        dData[offset + tid] = out1;
        dData[offset + tid + l / 2] = out2;
    }

    [GpuDevice]
    public static void Device_NTT_PointwiseSquare(uint[] dData1, uint[] dData2, int n)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < n; i += GridDimension.X * BlockDimension.X)
        {
            dData1[i] = GpuBigIntNttKernels.ModMulBarrett(dData1[i], dData1[i], GpuBigIntNttKernels.NttP1,
                GpuBigIntNttKernels.NttM1);
            dData2[i] = GpuBigIntNttKernels.ModMulBarrett(dData2[i], dData2[i], GpuBigIntNttKernels.NttP2,
                GpuBigIntNttKernels.NttM2);
        }
    }

    [GpuDevice]
    public static void Device_CRT_ParallelAtomic([GpuConst] uint[] dRes1, [GpuConst] uint[] dRes2, uint[] outUint, int n, int outNumWords)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        
        var invN1 = GpuBigIntNttKernels.ModPow((uint)n, GpuBigIntNttKernels.NttP1 - 2, GpuBigIntNttKernels.NttP1,
            GpuBigIntNttKernels.NttM1);
        var invN2 = GpuBigIntNttKernels.ModPow((uint)n, GpuBigIntNttKernels.NttP2 - 2, GpuBigIntNttKernels.NttP2,
            GpuBigIntNttKernels.NttM2);

        for (var k = tid; k < n; k += GridDimension.X * BlockDimension.X)
        {
            const ulong P1 = GpuBigIntNttKernels.NttP1;
            const ulong P2 = GpuBigIntNttKernels.NttP2;

            ulong v1 = GpuBigIntNttKernels.ModMulBarrett(dRes1[k], invN1, GpuBigIntNttKernels.NttP1,
                GpuBigIntNttKernels.NttM1);
            ulong v2 = GpuBigIntNttKernels.ModMulBarrett(dRes2[k], invN2, GpuBigIntNttKernels.NttP2,
                GpuBigIntNttKernels.NttM2);

            ulong diff;
            if (v2 >= v1)
            {
                diff = v2 - v1;
            }
            else
            {
                diff = v2 + P2 - v1;
            }

            // CRT recombination
            // x = v1 + P1 * [(v2 - v1) * (P1^-1 mod P2) mod P2]
            // p1InvP2 = P1^-1 mod P2
            // P1 = 4253024257, P2 = 4293918721
            const ulong p1InvP2 = 105UL;
            var h = (diff * p1InvP2) % P2;
            var val = v1 + P1 * h;

            if (val <= 0)
            {
                continue;
            }

            var wordIdx = k / 2;
            var bitOffset = (k % 2) * 16;

            uint low32;
            uint mid32;
            uint high32 = 0;

            if (bitOffset == 0)
            {
                low32 = (uint)(val & 0xFFFFFFFF);
                mid32 = (uint)(val >> 32);
            }
            else
            {
                low32 = (uint)((val << 16) & 0xFFFFFFFF);
                mid32 = (uint)((val >> 16) & 0xFFFFFFFF);
                high32 = (uint)(val >> 48);
            }

            if (low32 != 0)
            {
                GpuBigIntNttKernels.AtomicAddWithCarry(outUint, wordIdx, low32, outNumWords);
            }

            if (mid32 != 0)
            {
                GpuBigIntNttKernels.AtomicAddWithCarry(outUint, wordIdx + 1, mid32, outNumWords);
            }

            if (high32 != 0)
            {
                GpuBigIntNttKernels.AtomicAddWithCarry(outUint, wordIdx + 2, high32, outNumWords);
            }
        }
    }

    [GpuDevice]
    private static void Device_NTT_Internal(ref NttKernelArgs args)
    {
        var buf = args.Buffer;
        var tmp = args.TemporaryBuffer;
        var m = args.M;
        var n1 = args.N1;
        var n2 = args.N2;
        var isInverse = args.IsInverse;
        if (m <= 512)
        {
            args.CurrentL = m;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            Device_NTT_Shared_Row(ref args);
            Gpu.SyncGrid();
        }
        else
        {
            if (isInverse)
            {
                args.CurrentL = n2;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = true;
                args.MaxBlocks = n1;
                Device_NTT_Shared_Row(ref args);
                Gpu.SyncGrid();
                
                Device_Transpose_32x32(buf, tmp, n2, n1);
                Gpu.SyncGrid();
                
                args.Buffer = tmp;
                args.CurrentL = n1;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = false;
                args.MaxBlocks = n2;
                Device_NTT_Shared_Row(ref args);
                args.Buffer = buf; // Restore buffer
                Gpu.SyncGrid();
                
                Device_Transpose_32x32(tmp, buf, n1, n2);
                Gpu.SyncGrid();
            }
            else
            {
                Device_Transpose_32x32(buf, tmp, n2, n1);
                Gpu.SyncGrid();
                
                args.Buffer = tmp;
                args.CurrentL = n1;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = true;
                args.MaxBlocks = n2;
                Device_NTT_Shared_Row(ref args);
                args.Buffer = buf; // Restore buffer
                Gpu.SyncGrid();
                
                Device_Transpose_32x32(tmp, buf, n1, n2);
                Gpu.SyncGrid();
                
                args.CurrentL = n2;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = false;
                args.MaxBlocks = n1;
                Device_NTT_Shared_Row(ref args);
                Gpu.SyncGrid();
            }
        }
    }

    [GpuDevice]
    public static void Device_ForwardNTT(ref NttKernelArgs args)
    {
        args.IsInverse = false;
        Device_NTT_Internal(ref args);
    }

    [GpuDevice]
    public static void Device_InverseNTT(ref NttKernelArgs args)
    {
        args.IsInverse = true;
        Device_NTT_Internal(ref args);
    }

    [GpuDevice]
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

        var nttArgs = new NttKernelArgs
        {
            Buffer = ctx.Ntt.Buffers.Buffer1,
            TemporaryBuffer = ctx.Ntt.Buffers.TemporaryBuffer,
            Twiddle = ctx.Ntt.TwiddleFactors.Forward1,
            M = ctx.Ntt.TransformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.NttP1,
            Pm = GpuBigIntNttKernels.NttM1
        };

        // Forward NTT 1
        Device_ForwardNTT(ref nttArgs);

        // Forward NTT 2
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer2;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Forward2;
        nttArgs.P = GpuBigIntNttKernels.NttP2;
        nttArgs.Pm = GpuBigIntNttKernels.NttM2;
        Device_ForwardNTT(ref nttArgs);

        Device_NTT_PointwiseSquare(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2, ctx.Ntt.TransformLength);
        Gpu.SyncGrid();

        // Inverse NTT 1
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer1;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Inverse1;
        nttArgs.P = GpuBigIntNttKernels.NttP1;
        nttArgs.Pm = GpuBigIntNttKernels.NttM1;
        Device_InverseNTT(ref nttArgs);

        // Inverse NTT 2
        nttArgs.Buffer = ctx.Ntt.Buffers.Buffer2;
        nttArgs.Twiddle = ctx.Ntt.TwiddleFactors.Inverse2;
        nttArgs.P = GpuBigIntNttKernels.NttP2;
        nttArgs.Pm = GpuBigIntNttKernels.NttM2;
        Device_InverseNTT(ref nttArgs);
        Gpu.SyncGrid();

        var actualOutNumWords = ctx.Dimensions.NumberOfWords * 2 + 16;
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < actualOutNumWords; i += GridDimension.X * BlockDimension.X)
        {
            ctx.Buffers.ValueT[i] = 0;
        }

        Gpu.SyncGrid();

        Device_CRT_ParallelAtomic(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2, ctx.Buffers.ValueT,
            ctx.Ntt.TransformLength, ctx.Dimensions.NumberOfWords * 2);
        Gpu.SyncGrid();
    }

    [GpuDevice]
    private static void Device_AddSub(ref BigIntKernelContext ctx, bool isSubtract)
    {
        var dA = ctx.Operands.LeftOperand;
        var dB = ctx.Operands.RightOperand;
        var dResult = ctx.Operands.ResultBuffer;
        var carryBuffer = ctx.Operands.CarryBuffer;
        var numWords = ctx.NumberOfWords;
        if (BlockIndex.X == 0 && ThreadIndex.X == 0)
        {
            carryBuffer[0] = 0;
        }

        Gpu.SyncGrid();

        int chunkSize = 256;
        var chunkIdx = BlockIndex.X;
        var start = chunkIdx * chunkSize;

        if (start < numWords && ThreadIndex.X < chunkSize)
        {
            var end = start + chunkSize;
            if (end > numWords)
            {
                end = numWords;
            }

            if (ThreadIndex.X == 0)
            {
                ulong localCarry = 0;
                if (isSubtract)
                {
                    if (chunkIdx == 0)
                    {
                        localCarry = 1;
                    }
                }

                for (var i = start; i < end; i++)
                {
                    uint bVal;
                    if (isSubtract)
                    {
                        bVal = ~dB[i];
                    }
                    else
                    {
                        bVal = dB[i];
                    }

                    var current = dA[i] + (ulong)bVal + localCarry;
                    dResult[i] = (uint)(current & 0xFFFFFFFF);
                    localCarry = current >> 32;
                }

                carryBuffer[chunkIdx + 1] = (long)localCarry;
            }
        }

        Gpu.SyncGrid();

        if (BlockIndex.X == 0 && ThreadIndex.X == 0)
        {
            var numChunks = (numWords + chunkSize - 1) / chunkSize;
            ulong rippleCarry = 0;
            for (int cIdx = 0; cIdx < numChunks; cIdx++)
            {
                var s = cIdx * chunkSize;
                var e = s + chunkSize;
                if (e > numWords)
                {
                    e = numWords;
                }

                rippleCarry += (ulong)carryBuffer[cIdx];
                if (rippleCarry == 0)
                {
                    continue;
                }

                for (var i = s; i < e; i++)
                {
                    var current = dResult[i] + rippleCarry;
                    dResult[i] = (uint)(current & 0xFFFFFFFF);
                    rippleCarry = current >> 32;
                    if (rippleCarry == 0)
                    {
                        break;
                    }
                }
            }
        }

        Gpu.SyncGrid();
    }

    [GpuDevice]
    public static void Device_Add(ref BigIntKernelContext ctx)
        => Device_AddSub(ref ctx, false);

    [GpuDevice]
    public static void Device_Sub(ref BigIntKernelContext ctx)
        => Device_AddSub(ref ctx, true);

    [GpuDevice]
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
            int srcIdx0 = 0;
            if (isRight)
            {
                srcIdx0 = i + wordShift;
            }
            else
            {
                srcIdx0 = i - wordShift;
            }

            int srcIdx1 = 0;
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

    [GpuDevice]
    public static void Device_ShiftLeft(ref BigIntKernelContext ctx, int shiftBits)
    {
        Device_ShiftLeftRight(ref ctx, shiftBits, false);
    }

    [GpuDevice]
    public static void Device_ShiftRight(ref BigIntKernelContext ctx, int shiftBits)
    {
        Device_ShiftLeftRight(ref ctx, shiftBits, true);
    }


    [GpuDevice]
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


    [GpuDevice, GpuInline]
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

    [GpuDevice, GpuInline]
    public static void Device_MultiplySquareAndReduce(int reduceCount, ref FermatContext ctx)
    {
        Device_MultiplySquare(ref ctx);
        for (int j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(ref ctx);
        }
    }

    [GpuDevice, GpuInline]
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
    public static void Kernel_StaticFermatLoop(ref FermatContext ctx, [JitConstant] string GSHARP_EXPONENT, [JitConstant] string GSHARP_EXPONENT_MINUS_4)
    {
        for (int iter = 0; iter < Gpu.JitConstant(GSHARP_EXPONENT); iter++)
        {
            Device_MultiplySquareAndReduce(3, ref ctx);
            Device_ShiftLeftAndReduce(3, ref ctx);
        }

        for (int iter = 0; iter < Gpu.JitConstant(GSHARP_EXPONENT_MINUS_4); iter++)
        {
            Device_MultiplySquareAndReduce(3, ref ctx);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, ref ctx);
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, ref ctx);

        for (int iter = 0; iter < Gpu.JitConstant(GSHARP_EXPONENT_MINUS_4); iter++)
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
