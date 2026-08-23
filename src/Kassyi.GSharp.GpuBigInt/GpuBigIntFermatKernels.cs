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

        for (var i = 0; i < 32; i += 8)
        {
            if (xIndex < width && yIndex + i < height)
            {
                Gpu.InlinePtx($"s_mem[({ty} + {i}) * 33 + {tx}] = {idata[(yIndex + i) * width + xIndex]};");
            }
        }

        Gpu.SyncThreads();

        xIndex = by * 32 + tx;
        yIndex = bx * 32 + ty;

        for (var i = 0; i < 32; i += 8)
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
    public static void Device_NTT_Shared_Row(uint[] dData, [GpuConst] uint[] dTwiddle, int n, int l, uint p, ulong m,
        bool applyTwiddleBefore, bool applyTwiddleAfter, int maxBlocks)
    {
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

        var log2L = 0;
        while ((1 << log2L) < l)
        {
            log2L++;
        }

        var revTid1 = 0;
        var revTid2 = 0;
        for (var i = 0; i < log2L; i++)
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

        for (var s = 0; s < log2L; s++)
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
    private static void Device_NTT_Internal(uint[] buf, uint[] tmp, [GpuConst] uint[] twiddle, int m, int n1, int n2, uint p,
        ulong pm, bool isInverse)
    {
        if (m <= 512)
        {
            Device_NTT_Shared_Row(buf, twiddle, m, m, p, pm, false, false, 1);
            Gpu.SyncGrid();
        }
        else
        {
            if (isInverse)
            {
                Device_NTT_Shared_Row(buf, twiddle, m, n2, p, pm, false, true, n1);
                Gpu.SyncGrid();
                Device_Transpose_32x32(buf, tmp, n2, n1);
                Gpu.SyncGrid();
                Device_NTT_Shared_Row(tmp, twiddle, m, n1, p, pm, false, false, n2);
                Gpu.SyncGrid();
                Device_Transpose_32x32(tmp, buf, n1, n2);
                Gpu.SyncGrid();
            }
            else
            {
                Device_Transpose_32x32(buf, tmp, n2, n1);
                Gpu.SyncGrid();
                Device_NTT_Shared_Row(tmp, twiddle, m, n1, p, pm, false, true, n2);
                Gpu.SyncGrid();
                Device_Transpose_32x32(tmp, buf, n1, n2);
                Gpu.SyncGrid();
                Device_NTT_Shared_Row(buf, twiddle, m, n2, p, pm, false, false, n1);
                Gpu.SyncGrid();
            }
        }
    }

    [GpuDevice]
    public static void Device_ForwardNTT(uint[] buf, uint[] tmp, [GpuConst] uint[] twiddle, int m, int n1, int n2, uint p,
        ulong pm)
        => Device_NTT_Internal(buf, tmp, twiddle, m, n1, n2, p, pm, false);

    [GpuDevice]
    public static void Device_InverseNTT(uint[] buf, uint[] tmp, [GpuConst] uint[] twiddle, int m, int n1, int n2, uint p,
        ulong pm)
        => Device_NTT_Internal(buf, tmp, twiddle, m, n1, n2, p, pm, true);

    [GpuDevice]
    public static void Device_MultiplySquare(ref FermatContext ctx)
    {
        Device_Unpack16(ctx.Buffers.ValueT, ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2, ctx.Ntt.TransformLength,
            ctx.Dimensions.NumberOfWords);
        Gpu.SyncGrid();

        var n2 = 1;
        while (n2 * n2 < ctx.Ntt.TransformLength)
        {
            n2 *= 2;
        }

        var n1 = ctx.Ntt.TransformLength / n2;

        // Forward NTT 1
        Device_ForwardNTT(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Ntt.TwiddleFactors.Forward1,
            ctx.Ntt.TransformLength, n1, n2, GpuBigIntNttKernels.NttP1,
            GpuBigIntNttKernels.NttM1);

        // Forward NTT 2
        Device_ForwardNTT(ctx.Ntt.Buffers.Buffer2, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Ntt.TwiddleFactors.Forward2,
            ctx.Ntt.TransformLength, n1, n2, GpuBigIntNttKernels.NttP2,
            GpuBigIntNttKernels.NttM2);

        Device_NTT_PointwiseSquare(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.Buffer2, ctx.Ntt.TransformLength);
        Gpu.SyncGrid();

        // Inverse NTT 1
        Device_InverseNTT(ctx.Ntt.Buffers.Buffer1, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Ntt.TwiddleFactors.Inverse1,
            ctx.Ntt.TransformLength, n1, n2, GpuBigIntNttKernels.NttP1,
            GpuBigIntNttKernels.NttM1);

        // Inverse NTT 2
        Device_InverseNTT(ctx.Ntt.Buffers.Buffer2, ctx.Ntt.Buffers.TemporaryBuffer, ctx.Ntt.TwiddleFactors.Inverse2,
            ctx.Ntt.TransformLength, n1, n2, GpuBigIntNttKernels.NttP2,
            GpuBigIntNttKernels.NttM2);
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
    private static void Device_AddSub([GpuConst] uint[] dA, [GpuConst] uint[] dB, uint[] dResult, int numWords, long[] carryBuffer,
        bool isSubtract)
    {
        if (BlockIndex.X == 0 && ThreadIndex.X == 0)
        {
            carryBuffer[0] = 0;
        }

        Gpu.SyncGrid();

        var chunkSize = 256;
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
            for (var cIdx = 0; cIdx < numChunks; cIdx++)
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
    public static void Device_Add([GpuConst] uint[] dA, [GpuConst] uint[] dB, uint[] dResult, int numWords, long[] carryBuffer)
        => Device_AddSub(dA, dB, dResult, numWords, carryBuffer, false);

    [GpuDevice]
    public static void Device_Sub([GpuConst] uint[] dA, [GpuConst] uint[] dB, uint[] dResult, int numWords, long[] carryBuffer)
        => Device_AddSub(dA, dB, dResult, numWords, carryBuffer, true);

    [GpuDevice]
    private static void Device_ShiftLeftRight([GpuConst] uint[] dA, int shiftBits, uint[] dResult, int numWords, bool isRight)
    {
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
    public static void Device_ShiftLeft([GpuConst] uint[] dA, int shiftBits, uint[] dResult, int numWords)
    {
        Device_ShiftLeftRight(dA, shiftBits, dResult, numWords, false);
    }

    [GpuDevice]
    public static void Device_ShiftRight([GpuConst] uint[] dA, int shiftBits, uint[] dResult, int numWords)
    {
        Device_ShiftLeftRight(dA, shiftBits, dResult, numWords, true);
    }

    [GpuDevice]
    public static void Device_BitwiseAnd([GpuConst] uint[] dA, [GpuConst] uint[] dB, uint[] dResult, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            dResult[i] = dA[i] & dB[i];
        }

        Gpu.SyncGrid();
    }

    [GpuDevice]
    public static void Device_Copy([GpuConst] uint[] dSrc, uint[] dDst, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var tid = i; tid < numWords; tid += GridDimension.X * BlockDimension.X)
        {
            dDst[tid] = dSrc[tid];
        }

        Gpu.SyncGrid();
    }

    [GpuDevice]
    public static void Device_MultiplySquare(uint[] valueT, uint[] nttBuffer1, uint[] nttBuffer2, uint[] nttTemp, 
        [GpuConst] uint[] t1Fwd, [GpuConst] uint[] t2Fwd, [GpuConst] uint[] t1Inv, [GpuConst] uint[] t2Inv, 
        int transformLength, int numberOfWords)
    {
        Device_Unpack16(valueT, nttBuffer1, nttBuffer2, transformLength, numberOfWords);
        Gpu.SyncGrid();

        var n2 = 1;
        while (n2 * n2 < transformLength)
        {
            n2 *= 2;
        }

        var n1 = transformLength / n2;

        Device_ForwardNTT(nttBuffer1, nttTemp, t1Fwd, transformLength, n1, n2, GpuBigIntNttKernels.NttP1, GpuBigIntNttKernels.NttM1);
        Device_ForwardNTT(nttBuffer2, nttTemp, t2Fwd, transformLength, n1, n2, GpuBigIntNttKernels.NttP2, GpuBigIntNttKernels.NttM2);

        Device_NTT_PointwiseSquare(nttBuffer1, nttBuffer2, transformLength);
        Gpu.SyncGrid();

        Device_InverseNTT(nttBuffer1, nttTemp, t1Inv, transformLength, n1, n2, GpuBigIntNttKernels.NttP1, GpuBigIntNttKernels.NttM1);
        Device_InverseNTT(nttBuffer2, nttTemp, t2Inv, transformLength, n1, n2, GpuBigIntNttKernels.NttP2, GpuBigIntNttKernels.NttM2);
        Gpu.SyncGrid();

        var actualOutNumWords = numberOfWords * 2 + 16;
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < actualOutNumWords; i += GridDimension.X * BlockDimension.X)
        {
            valueT[i] = 0;
        }

        Gpu.SyncGrid();

        Device_CRT_ParallelAtomic(nttBuffer1, nttBuffer2, valueT, transformLength, numberOfWords * 2);
        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_FermatReduceStep(
        uint[] valueT, uint[] quotient, uint[] remainder, [GpuConst] uint[] remainderMask, uint[] temporaryBuffer,
        int numberOfWordsExtended, int exponent, int bitOffset2E1, long[] carryBuffer)
    {
        Device_ShiftRight(valueT, bitOffset2E1, quotient, numberOfWordsExtended);
        Device_BitwiseAnd(valueT, remainderMask, remainder, numberOfWordsExtended);
        Device_ShiftLeft(quotient, exponent, temporaryBuffer, numberOfWordsExtended);
        Device_Add(remainder, temporaryBuffer, remainder, numberOfWordsExtended, carryBuffer);
        Device_ShiftLeft(quotient, 1, temporaryBuffer, numberOfWordsExtended);
        Device_Sub(remainder, quotient, remainder, numberOfWordsExtended, carryBuffer);
        Device_Sub(remainder, temporaryBuffer, remainder, numberOfWordsExtended, carryBuffer);
        Device_ShiftLeft(quotient, 3, temporaryBuffer, numberOfWordsExtended);
        Device_Sub(remainder, temporaryBuffer, valueT, numberOfWordsExtended, carryBuffer);
    }

    [GpuDevice, GpuInline]
    public static void Device_MultiplySquareAndReduce(int reduceCount, 
        uint[] valueT, uint[] quotient, uint[] remainder, [GpuConst] uint[] remainderMask, uint[] temporaryBuffer,
        int numberOfWords, int numberOfWordsExtended, int exponent, int bitOffset2E1, long[] carryBuffer,
        uint[] nttBuffer1, uint[] nttBuffer2, uint[] nttTemp, 
        [GpuConst] uint[] t1Fwd, [GpuConst] uint[] t2Fwd, [GpuConst] uint[] t1Inv, [GpuConst] uint[] t2Inv, int transformLength)
    {
        Device_MultiplySquare(valueT, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength, numberOfWords);
        for (var j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer);
        }
    }

    [GpuDevice, GpuInline]
    public static void Device_ShiftLeftAndReduce(int reduceCount, 
        uint[] valueT, uint[] quotient, uint[] remainder, [GpuConst] uint[] remainderMask, uint[] temporaryBuffer,
        int numberOfWordsExtended, int exponent, int bitOffset2E1, long[] carryBuffer)
    {
        Device_ShiftLeft(valueT, 1, temporaryBuffer, numberOfWordsExtended);
        Device_Copy(temporaryBuffer, valueT, numberOfWordsExtended);
        for (var j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer);
        }
    }

    [GpuKernel(Cooperative = true)]
    public static void Kernel_StaticFermatLoop(ref FermatContext ctx, [JitConstant] string GSHARP_EXPONENT, [JitConstant] string GSHARP_EXPONENT_MINUS_4)
    {
        var valueT = ctx.Buffers.ValueT;
        var quotient = ctx.Buffers.Quotient;
        var remainder = ctx.Buffers.Remainder;
        var remainderMask = ctx.Buffers.RemainderMask;
        var temporaryBuffer = ctx.Buffers.TemporaryBuffer;
        var carryBuffer = ctx.Buffers.CarryBuffer;
        
        var numberOfWords = ctx.Dimensions.NumberOfWords;
        var numberOfWordsExtended = ctx.Dimensions.NumberOfWordsExtended;
        var exponent = ctx.Dimensions.Exponent;
        var bitOffset2E1 = ctx.Dimensions.BitOffset2E1;
        
        var nttBuffer1 = ctx.Ntt.Buffers.Buffer1;
        var nttBuffer2 = ctx.Ntt.Buffers.Buffer2;
        var nttTemp = ctx.Ntt.Buffers.TemporaryBuffer;
        var transformLength = ctx.Ntt.TransformLength;
        var t1Fwd = ctx.Ntt.TwiddleFactors.Forward1;
        var t2Fwd = ctx.Ntt.TwiddleFactors.Forward2;
        var t1Inv = ctx.Ntt.TwiddleFactors.Inverse1;
        var t2Inv = ctx.Ntt.TwiddleFactors.Inverse2;

        for (var iter = 0; iter < Gpu.JitConstant(GSHARP_EXPONENT); iter++)
        {
            Device_MultiplySquareAndReduce(3, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
            Device_ShiftLeftAndReduce(3, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer);
        }

        for (var iter = 0; iter < Gpu.JitConstant(GSHARP_EXPONENT_MINUS_4); iter++)
        {
            Device_MultiplySquareAndReduce(3, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
        Device_ShiftLeftAndReduce(1, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);

        for (var iter = 0; iter < Gpu.JitConstant(GSHARP_EXPONENT_MINUS_4); iter++)
        {
            Device_MultiplySquareAndReduce(3, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
        Device_ShiftLeftAndReduce(1, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);

        // bit 1 (1)
        Device_MultiplySquareAndReduce(9, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
        Device_ShiftLeftAndReduce(1, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer);

        // bit 0 (0)
        Device_MultiplySquareAndReduce(9, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
    }
}
