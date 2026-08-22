using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.GpuBigInt;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntFermatKernels
{
    [GpuDevice, GpuInline]
    public static void Device_Unpack16(uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords)
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

    [GpuDevice, GpuInline]
    public static void Device_Transpose_32x32(uint[] idata, uint[] odata, int width, int height)
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

    [GpuDevice, GpuInline]
    public static void Device_NTT_Shared_Row(uint[] dData, uint[] dTwiddle, int n, int l, uint p, ulong m,
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

    [GpuDevice, GpuInline]
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

    [GpuDevice, GpuInline]
    public static void Device_CRT_ParallelAtomic(uint[] dRes1, uint[] dRes2, uint[] outUint, int n, int outNumWords)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var k = tid; k < n; k += GridDimension.X * BlockDimension.X)
        {
            const ulong P1 = GpuBigIntNttKernels.NttP1;
            const ulong P2 = GpuBigIntNttKernels.NttP2;
            var p1InvP2 = 4246732126UL;

            var invN1 = GpuBigIntNttKernels.ModPow((uint)n, GpuBigIntNttKernels.NttP1 - 2, GpuBigIntNttKernels.NttP1,
                GpuBigIntNttKernels.NttM1);
            var invN2 = GpuBigIntNttKernels.ModPow((uint)n, GpuBigIntNttKernels.NttP2 - 2, GpuBigIntNttKernels.NttP2,
                GpuBigIntNttKernels.NttM2);

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

    [GpuDevice, GpuInline]
    public static void Device_ForwardNTT(uint[] buf, uint[] tmp, uint[] twiddle, int m, int n1, int n2, uint p,
        ulong pm)
    {
        if (m <= 512)
        {
            Device_NTT_Shared_Row(buf, twiddle, m, m, p, pm, false, false, 1);
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

    [GpuDevice, GpuInline]
    public static void Device_InverseNTT(uint[] buf, uint[] tmp, uint[] twiddle, int m, int n1, int n2, uint p,
        ulong pm)
    {
        if (m <= 512)
        {
            Device_NTT_Shared_Row(buf, twiddle, m, m, p, pm, false, false, 1);
            Gpu.SyncGrid();
        }
        else
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
    }

    [GpuKernel(Cooperative = true)]
    public static void Device_MultiplySquare(uint[] data, int numWords, int m, uint[] nttBuf1, uint[] nttBuf2,
        uint[] nttTmp, uint[] twiddle1Fwd, uint[] twiddle2Fwd, uint[] twiddle1Inv, uint[] twiddle2Inv)
    {
        Device_Unpack16(data, nttBuf1, nttBuf2, m, numWords);
        Gpu.SyncGrid();

        var n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var n1 = m / n2;

        // Forward NTT 1
        Device_ForwardNTT(nttBuf1, nttTmp, twiddle1Fwd, m, n1, n2, GpuBigIntNttKernels.NttP1,
            GpuBigIntNttKernels.NttM1);

        // Forward NTT 2
        Device_ForwardNTT(nttBuf2, nttTmp, twiddle2Fwd, m, n1, n2, GpuBigIntNttKernels.NttP2,
            GpuBigIntNttKernels.NttM2);

        Device_NTT_PointwiseSquare(nttBuf1, nttBuf2, m);
        Gpu.SyncGrid();

        // Inverse NTT 1
        Device_InverseNTT(nttBuf1, nttTmp, twiddle1Inv, m, n1, n2, GpuBigIntNttKernels.NttP1,
            GpuBigIntNttKernels.NttM1);

        // Inverse NTT 2
        Device_InverseNTT(nttBuf2, nttTmp, twiddle2Inv, m, n1, n2, GpuBigIntNttKernels.NttP2,
            GpuBigIntNttKernels.NttM2);
        Gpu.SyncGrid();

        var actualOutNumWords = numWords * 2 + 16;
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < actualOutNumWords; i += GridDimension.X * BlockDimension.X)
        {
            data[i] = 0;
        }

        Gpu.SyncGrid();

        Device_CRT_ParallelAtomic(nttBuf1, nttBuf2, data, m, numWords * 2);
        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_Add(uint[] dA, uint[] dB, uint[] dResult, int numWords, long[] carryBuffer)
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
                for (var i = start; i < end; i++)
                {
                    var current = dA[i] + dB[i] + localCarry;
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

    [GpuDevice, GpuInline]
    public static void Device_Sub(uint[] dA, uint[] dB, uint[] dResult, int numWords, long[] carryBuffer)
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
                long localBorrow = 0;
                for (var i = start; i < end; i++)
                {
                    // ReSharper disable once RedundantCast
                    var current = (long)dA[i] - dB[i] - localBorrow;
                    if (current < 0)
                    {
                        current += 0x100000000L;
                        localBorrow = 1;
                    }
                    else
                    {
                        localBorrow = 0;
                    }

                    dResult[i] = (uint)(current & 0xFFFFFFFF);
                }

                carryBuffer[chunkIdx + 1] = localBorrow;
            }
        }

        Gpu.SyncGrid();

        if (BlockIndex.X == 0 && ThreadIndex.X == 0)
        {
            var numChunks = (numWords + chunkSize - 1) / chunkSize;
            long rippleBorrow = 0;
            for (var cIdx = 0; cIdx < numChunks; cIdx++)
            {
                var s = cIdx * chunkSize;
                var e = s + chunkSize;
                if (e > numWords)
                {
                    e = numWords;
                }

                rippleBorrow += carryBuffer[cIdx];
                if (rippleBorrow == 0)
                {
                    continue;
                }

                for (var i = s; i < e; i++)
                {
                    var current = dResult[i] - rippleBorrow;
                    if (current < 0)
                    {
                        current += 0x100000000L;
                        rippleBorrow = 1;
                    }
                    else
                    {
                        rippleBorrow = 0;
                    }

                    dResult[i] = (uint)(current & 0xFFFFFFFF);
                    if (rippleBorrow == 0)
                    {
                        break;
                    }
                }
            }
        }

        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_ShiftLeft(uint[] dA, int shiftBits, uint[] dResult, int numWords)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;
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
                dResult[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
            }
        }

        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_ShiftRight(uint[] dA, int shiftBits, uint[] dResult, int numWords)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;
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
                dResult[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
            }
        }

        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_BitwiseAnd(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < numWords)
        {
            dResult[i] = dA[i] & dB[i];
        }

        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_Copy(uint[] dSrc, uint[] dDst, int numWords)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var tid = i; tid < numWords; tid += GridDimension.X * BlockDimension.X)
        {
            dDst[tid] = dSrc[tid];
        }

        Gpu.SyncGrid();
    }

    [GpuDevice, GpuInline]
    public static void Device_FermatReduceStep(
        uint[] t, uint[] q, uint[] r, uint[] maskR,
        uint[] tTmp, int numWordsExtended, int e, int bit2E1, long[] carryBuffer)
    {
        Device_ShiftRight(t, bit2E1, q, numWordsExtended);
        Device_BitwiseAnd(t, maskR, r, numWordsExtended);
        Device_ShiftLeft(q, e, tTmp, numWordsExtended);
        Device_Add(r, tTmp, r, numWordsExtended, carryBuffer);
        Device_ShiftLeft(q, 1, tTmp, numWordsExtended);
        Device_Sub(r, q, r, numWordsExtended, carryBuffer);
        Device_Sub(r, tTmp, r, numWordsExtended, carryBuffer);
        Device_ShiftLeft(q, 3, tTmp, numWordsExtended);
        Device_Sub(r, tTmp, t, numWordsExtended, carryBuffer);
    }

    private struct FermatContext
    {
        public uint[] T;
        public uint[] Q;
        public uint[] R;
        public uint[] MaskR;
        public uint[] TTmp;
        public int NumWords;
        public int NumWordsExtended;
        public int E;
        public int Bit2E1;
        public long[] CarryBuffer;
        public int M;
        public uint[] NttBuf1;
        public uint[] NttBuf2;
        public uint[] NttTmp;
        public uint[] Twiddle1Fwd;
        public uint[] Twiddle2Fwd;
        public uint[] Twiddle1Inv;
        public uint[] Twiddle2Inv;
    }

    [GpuDevice, GpuInline]
    public static void Device_MultiplySquareAndReduce(int reduceCount, ref FermatContext ctx)
    {
        Device_MultiplySquare(ctx.T, ctx.NumWords, ctx.M, ctx.NttBuf1, ctx.NttBuf2, ctx.NttTmp, ctx.Twiddle1Fwd,
            ctx.Twiddle2Fwd, ctx.Twiddle1Inv, ctx.Twiddle2Inv);
        for (var j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(ctx.T, ctx.Q, ctx.R, ctx.MaskR, ctx.TTmp, ctx.NumWordsExtended, ctx.E, ctx.Bit2E1,
                ctx.CarryBuffer);
        }
    }

    [GpuDevice, GpuInline]
    public static void Device_ShiftLeftAndReduce(int reduceCount, ref FermatContext ctx)
    {
        Device_ShiftLeft(ctx.T, 1, ctx.TTmp, ctx.NumWordsExtended);
        Device_Copy(ctx.TTmp, ctx.T, ctx.NumWordsExtended);
        for (var j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(ctx.T, ctx.Q, ctx.R, ctx.MaskR, ctx.TTmp, ctx.NumWordsExtended, ctx.E, ctx.Bit2E1,
                ctx.CarryBuffer);
        }
    }

    [GpuKernel(Cooperative = true)]
    public static void Kernel_StaticFermatLoop(
        uint[] t, uint[] q, uint[] r, uint[] maskR,
        uint[] tTmp,
        int numWords, int e, long[] carryBuffer,
        int m, uint[] nttBuf1, uint[] nttBuf2, uint[] nttTmp,
        uint[] twiddle1Fwd, uint[] twiddle2Fwd,
        uint[] twiddle1Inv, uint[] twiddle2Inv)
    {
        var ctx = new FermatContext
        {
            T = t,
            Q = q,
            R = r,
            MaskR = maskR,
            TTmp = tTmp,
            NumWords = numWords,
            NumWordsExtended = numWords * 2 + 16,
            E = e,
            Bit2E1 = 2 * e + 1,
            CarryBuffer = carryBuffer,
            M = m,
            NttBuf1 = nttBuf1,
            NttBuf2 = nttBuf2,
            NttTmp = nttTmp,
            Twiddle1Fwd = twiddle1Fwd,
            Twiddle2Fwd = twiddle2Fwd,
            Twiddle1Inv = twiddle1Inv,
            Twiddle2Inv = twiddle2Inv
        };

        for (var iter = 0; iter < ctx.E; iter++)
        {
            Device_MultiplySquareAndReduce(3, ref ctx);
            Device_ShiftLeftAndReduce(3, ref ctx);
        }

        for (var iter = 0; iter < ctx.E - 4; iter++)
        {
            Device_MultiplySquareAndReduce(3, ref ctx);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, ref ctx);
        Device_ShiftLeftAndReduce(1, ref ctx);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, ref ctx);

        for (var iter = 0; iter < ctx.E - 4; iter++)
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

