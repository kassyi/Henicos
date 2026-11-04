// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using static NttPrimeArithmetic;

/// <summary>
/// 35-bit limb unpack, pointwise multiply, division-free Garner CRT3 reconstruction, and blocked / 32x32 tiled carry
/// resolution kernels for the 3-prime 35-bit NTT convolution pipeline.
/// </summary>
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class Ntt35CarryKernels
{
    public struct Ntt35UnpackOperandContext
    {
        [GpuReadOnly]
        public uint[] Source;

        public uint[] NttBuf1;
        public uint[] NttBuf2;
        public uint[] NttBuf3;
        public int NumWords;
    }

    public struct Ntt35UnpackContext
    {
        public Ntt35UnpackOperandContext OperandA;
        public Ntt35UnpackOperandContext OperandB;
        public NttGridContext Grid;
    }

    public struct Ntt35UnpackSingleContext
    {
        public Ntt35UnpackOperandContext Operand;
        public NttGridContext Grid;
    }

    public struct Ntt35PointwiseMultiplyContext
    {
        public uint[] Buf1A;
        public uint[] Buf2A;
        public uint[] Buf3A;

        [GpuReadOnly]
        public uint[] Buf1B;

        [GpuReadOnly]
        public uint[] Buf2B;

        [GpuReadOnly]
        public uint[] Buf3B;

        public int TransformLength;
    }

    public struct Ntt35ResolveCarriesContext
    {
        [GpuReadOnly]
        public uint[] Result1;

        [GpuReadOnly]
        public uint[] Result2;

        [GpuReadOnly]
        public uint[] Result3;

        public uint[] OutputBuffer;
        public uint[] BlockCarries;
        public NttGridContext Grid;
        public int MaxWordCount;
        public int ChunkSize;
        public uint InvM1;
        public uint InvM2;
        public uint InvM3;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ExtractLimb35([GpuReadOnly] uint[] source, int numWords, int j)
    {
        ulong bitPos = (ulong)(uint)j * 35UL;
        int wordIdx = (int)(bitPos >> 5);
        if (wordIdx >= numWords)
        {
            return 0UL;
        }

        int bitOffset = (int)(bitPos & 31UL);
        ulong w0 = source[wordIdx];
        ulong w1 = wordIdx + 1 < numWords ? source[wordIdx + 1] : 0UL;
        if (bitOffset == 0)
        {
            return w0 | ((w1 & 0x7UL) << 32);
        }

        ulong w2 = (bitOffset > 29 && wordIdx + 2 < numWords) ? source[wordIdx + 2] : 0UL;
        return ((w0 >> bitOffset) | (w1 << (32 - bitOffset)) | (w2 << (64 - bitOffset))) & 0x7FFFFFFFFUL;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreLimb35(Ntt35UnpackOperandContext operand, int index, ulong limb)
    {
        operand.NttBuf1[index] = ModReduce64Barrett(limb, Prime1, BarrettMagic1);
        operand.NttBuf2[index] = ModReduce64Barrett(limb, Prime2, BarrettMagic2);
        operand.NttBuf3[index] = ModReduce64Barrett(limb, Prime3, BarrettMagic3);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExecuteUnpack35Small(Ntt35UnpackOperandContext operand, int m)
    {
        Gpu.GridStrideLoop(m, j =>
        {
            StoreLimb35(operand, j, ExtractLimb35(operand.Source, operand.NumWords, j));
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteUnpack35Tiled32x32(Ntt35UnpackOperandContext operand, NttGridContext grid)
    {
        int tid = ThreadIndex.X;
        int tx = tid & 31;
        int ty = tid >> 5;

        int tilesX = grid.N2 >> 5;
        int tilesY = grid.N1 >> 5;
        int totalTiles = tilesX * tilesY;
        int tilesXMask = tilesX - 1;
        int log2TilesX = 31 - (int)Gpu.CountLeadingZeros((uint)tilesX);
        int log2N2 = log2TilesX + 5;

        // 32 x 33 padded shared tile for 35-bit limbs (split into 32-bit low + 3-bit high = 8.25 KB, zero bank conflicts).
        var sTileLo = new SharedMemory<uint>(1056);
        var sTileHi = new SharedMemory<uint>(1056);

        for (int tileIdx = BlockIndex.X; tileIdx < totalTiles; tileIdx += GridDimension.X)
        {
            int bx = tileIdx & tilesXMask;
            int by = tileIdx >> log2TilesX;
            int r0 = bx << 5;
            int c0 = by << 5;

            int jIn = ((c0 + ty) << log2N2) + (r0 + tx);
            ulong valIn = ExtractLimb35(operand.Source, operand.NumWords, jIn);

            int inSlot = ty * 33 + tx;
            sTileLo[inSlot] = (uint)(valIn & 0xFFFFFFFFUL);
            sTileHi[inSlot] = (uint)(valIn >> 32);

            Gpu.SyncThreads();

            int dstIdx = (r0 + ty) * grid.N1 + (c0 + tx);
            int outSlot = tx * 33 + ty;
            ulong val = sTileLo[outSlot] | ((ulong)sTileHi[outSlot] << 32);
            StoreLimb35(operand, dstIdx, val);

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackSmallKernel(Ntt35UnpackContext ctx)
    {
        ExecuteUnpack35Small(ctx.OperandA, ctx.Grid.M);
        ExecuteUnpack35Small(ctx.OperandB, ctx.Grid.M);
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackSingleSmallKernel(Ntt35UnpackSingleContext ctx)
    {
        ExecuteUnpack35Small(ctx.Operand, ctx.Grid.M);
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackTiledKernel(Ntt35UnpackContext ctx)
    {
        ExecuteUnpack35Tiled32x32(ctx.OperandA, ctx.Grid);
        ExecuteUnpack35Tiled32x32(ctx.OperandB, ctx.Grid);
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackSingleTiledKernel(Ntt35UnpackSingleContext ctx)
    {
        ExecuteUnpack35Tiled32x32(ctx.Operand, ctx.Grid);
    }

    [GpuKernel]
    public static void MultiplyNtt35PointwiseMultiplyKernel(Ntt35PointwiseMultiplyContext ctx)
    {
        Gpu.GridStrideLoop(ctx.TransformLength, index =>
        {
            ctx.Buf1A[index] = ModMul1(ctx.Buf1A[index], ctx.Buf1B[index]);
            ctx.Buf2A[index] = ModMul2(ctx.Buf2A[index], ctx.Buf2B[index]);
            ctx.Buf3A[index] = ModMul3(ctx.Buf3A[index], ctx.Buf3B[index]);
        });
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GarnerCrt3Reconstruct96(
        Ntt35ResolveCarriesContext ctx, int srcIdx,
        out uint outLo, out uint outMid, out uint outHi)
    {
        uint v1 = ModMulBarrett(ctx.Result1[srcIdx], ctx.InvM1, Prime1, BarrettMagic1);
        uint v2 = ModMulBarrett(ctx.Result2[srcIdx], ctx.InvM2, Prime2, BarrettMagic2);
        uint v3 = ModMulBarrett(ctx.Result3[srcIdx], ctx.InvM3, Prime3, BarrettMagic3);

        uint v1Mod2 = v1 >= Prime2 ? v1 - Prime2 : v1;
        uint diff12 = ModSub(v2, v1Mod2, Prime2);
        uint k12 = ModMulBarrett(diff12, Prime1InvModPrime2, Prime2, BarrettMagic2);
        ulong x12 = (ulong)v1 + (ulong)Prime1 * (ulong)k12;

        uint v1Mod3 = v1 >= Prime3 ? v1 - Prime3 : v1;
        uint k12Mod3 = k12 >= Prime3 ? k12 - Prime3 : k12;
        uint term3 = ModMulBarrett(Prime1ModPrime3, k12Mod3, Prime3, BarrettMagic3);
        uint x12Mod3 = ModAdd(v1Mod3, term3, Prime3);
        uint diff3 = ModSub(v3, x12Mod3, Prime3);
        uint k3 = ModMulBarrett(diff3, Prime1Prime2InvModPrime3, Prime3, BarrettMagic3);

        ulong p1p2Const = Prime1TimesPrime2;
        ulong k3Wide = (ulong)k3;
        ulong prodLo = p1p2Const * k3Wide;
        ulong prodHi = 0UL;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({prodHi}) : \"l\"({p1p2Const}), \"l\"({k3Wide}));");

        ulong cLo = x12 + prodLo;
        outHi = (uint)prodHi + (cLo < x12 ? 1u : 0u);
        outLo = (uint)(cLo & 0xFFFFFFFFUL);
        outMid = (uint)(cLo >> 32);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SumCoefficientSlices(
        SharedMemory<uint> sCrtLo, SharedMemory<uint> sCrtMid, SharedMemory<uint> sCrtHi,
        int wordIdx, int jBlockStart, int m)
    {
        ulong bitStart = (ulong)(uint)wordIdx << 5;
        int jMin = bitStart <= 95UL ? 0 : (int)((bitStart - 61UL) / 35UL);
        int jMax = (int)((bitStart + 31UL) / 35UL);
        if (jMax >= m)
        {
            jMax = m - 1;
        }

        ulong localSum = 0UL;
        for (int j = jMin; j <= jMax; j++)
        {
            int localIdx = j - jBlockStart;
            ulong cLo = sCrtLo[localIdx] | ((ulong)sCrtMid[localIdx] << 32);
            uint cHi = sCrtHi[localIdx];
            ulong coeffBit = (ulong)(uint)j * 35UL;

            uint slice;
            if (coeffBit >= bitStart)
            {
                int leftShift = (int)(coeffBit - bitStart);
                slice = ((uint)cLo) << leftShift;
            }
            else
            {
                int rightShift = (int)(bitStart - coeffBit);
                if (rightShift < 64)
                {
                    slice = (uint)((cLo >> rightShift) | ((ulong)cHi << (64 - rightShift)));
                }
                else if (rightShift == 64)
                {
                    slice = cHi;
                }
                else
                {
                    slice = cHi >> (rightShift - 64);
                }
            }

            localSum += slice;
        }

        return localSum;
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNtt35CarriesBlocked(Ntt35ResolveCarriesContext ctx)
    {
        int tIdx = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int numBlocks = (ctx.MaxWordCount + ctx.ChunkSize - 1) / ctx.ChunkSize;

        var sharedCarry = new SharedMemory<uint>(32);
        var sharedH = new SharedMemory<uint>(32);
        // Any 1024-word output chunk spans at most ceil((32736 + 126) / 35) + 1 <= 940 35-bit limbs (< 960).
        var sCrtLo = new SharedMemory<uint>(960);
        var sCrtMid = new SharedMemory<uint>(960);
        var sCrtHi = new SharedMemory<uint>(960);

        for (int bIdx = BlockIndex.X; bIdx < numBlocks; bIdx += GridDimension.X)
        {
            int startWord = bIdx * ctx.ChunkSize;
            int lastWord = startWord + ctx.ChunkSize - 1;
            if (lastWord >= ctx.MaxWordCount)
            {
                lastWord = ctx.MaxWordCount - 1;
            }

            ulong blockBitStart = (ulong)(uint)startWord << 5;
            ulong blockBitEnd = (ulong)(uint)lastWord << 5;
            int jBlockStart = blockBitStart <= 95UL ? 0 : (int)((blockBitStart - 61UL) / 35UL);
            int jBlockEnd = (int)((blockBitEnd + 31UL) / 35UL);
            if (jBlockEnd >= ctx.Grid.M)
            {
                jBlockEnd = ctx.Grid.M - 1;
            }

            int jCount = jBlockEnd >= jBlockStart ? (jBlockEnd - jBlockStart + 1) : 0;

            for (int localJ = tIdx; localJ < jCount; localJ += bDim)
            {
                GarnerCrt3Reconstruct96(
                    ctx, jBlockStart + localJ,
                    out uint cLo, out uint cMid, out uint cHi);
                sCrtLo[localJ] = cLo;
                sCrtMid[localJ] = cMid;
                sCrtHi[localJ] = cHi;
            }

            Gpu.SyncThreads();

            int i = startWord + tIdx;
            ulong localSum = i < ctx.MaxWordCount
                ? SumCoefficientSlices(sCrtLo, sCrtMid, sCrtHi, i, jBlockStart, ctx.Grid.M)
                : 0UL;

            uint lowWord = (uint)(localSum & 0xFFFFFFFFUL);
            uint highWord = (uint)(localSum >> 32);

            int laneId = tIdx & 31;
            int warpId = tIdx >> 5;

            uint prevH = GpuBigIntNttKernels.ResolveOverlapH(highWord, sharedH, laneId, warpId);

            ulong sum = (ulong)lowWord + prevH;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = GpuBigIntNttKernels.ComputeBlockCarryScan(g, p, sharedCarry, laneId, warpId, bDim);

            if (i < ctx.MaxWordCount)
            {
                ctx.OutputBuffer[i] = (uint)sum + carryIn;
            }

            if (tIdx == 0)
            {
                int lastWarp = ((bDim + 31) >> 5) - 1;
                uint lastH = sharedH[lastWarp];
                uint waveCarry = GpuBigIntKernels.ApplyCarry(
                    GpuBigIntKernels.UnpackCarryState(sharedCarry[lastWarp]), 0u);
                ctx.BlockCarries[bIdx] = lastH + waveCarry;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ResolveNtt35CarriesBlockedKernel(Ntt35ResolveCarriesContext ctx)
    {
        ExecuteResolveNtt35CarriesBlocked(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LoadCrtTile(
        Ntt35ResolveCarriesContext ctx, int r0, int c0, int tx, int ty,
        SharedMemory<uint> sLo, SharedMemory<uint> sMid, SharedMemory<uint> sHi)
    {
        int cIn = c0 + tx;
        int srcIdx = (r0 + ty) * ctx.Grid.N1 + cIn;
        GarnerCrt3Reconstruct96(ctx, srcIdx, out uint loMain, out uint midMain, out uint hiMain);

        int mainSlot = tx * 35 + ty + 2;
        sLo[mainSlot] = loMain;
        sMid[mainSlot] = midMain;
        sHi[mainSlot] = hiMain;

        if (ty < 2)
        {
            uint loPrev = 0u;
            uint midPrev = 0u;
            uint hiPrev = 0u;
            if (r0 > 0)
            {
                int idxPrev = (r0 - 2 + ty) * ctx.Grid.N1 + cIn;
                GarnerCrt3Reconstruct96(ctx, idxPrev, out loPrev, out midPrev, out hiPrev);
            }
            else if (cIn > 0)
            {
                int idxPrev = (ctx.Grid.N2 - 2 + ty) * ctx.Grid.N1 + (cIn - 1);
                GarnerCrt3Reconstruct96(ctx, idxPrev, out loPrev, out midPrev, out hiPrev);
            }

            int prevSlot = tx * 35 + ty;
            sLo[prevSlot] = loPrev;
            sMid[prevSlot] = midPrev;
            sHi[prevSlot] = hiPrev;
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResolveTileDigits(
        SharedMemory<uint> sLo, SharedMemory<uint> sMid, SharedMemory<uint> sHi,
        int tx, int ty, out uint vLo, out uint vHi, out uint tileCarry)
    {
        int segBase = ty * 35;
        int slot0 = segBase + tx + 2;
        int slot1 = segBase + tx + 1;
        int slot2 = segBase + tx;

        ulong lo64Cur = sLo[slot0] | ((ulong)sMid[slot0] << 32);
        ulong part0 = lo64Cur & 0x7FFFFFFFFUL;

        ulong lo64Prev1 = sLo[slot1] | ((ulong)sMid[slot1] << 32);
        uint hiPrev1 = sHi[slot1];
        ulong part1 = ((lo64Prev1 >> 35) | ((ulong)hiPrev1 << 29)) & 0x7FFFFFFFFUL;

        ulong part2 = (ulong)(sHi[slot2] >> 6);

        ulong sum35 = part0 + part1 + part2;
        ulong l35 = sum35 & 0x7FFFFFFFFUL;
        uint h2 = (uint)(sum35 >> 35);

        uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, h2, 1u);
        if (tx == 0)
        {
            prevH = 0u;
        }

        ulong u35 = l35 + prevH;
        uint g = (uint)(u35 >> 35);
        ulong low35 = u35 & 0x7FFFFFFFFUL;
        uint p = low35 == 0x7FFFFFFFFUL ? 1u : 0u;

        var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), tx);
        uint carryIn = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Generate, 1u);
        if (tx == 0)
        {
            carryIn = 0u;
        }

        ulong finalV = (low35 + carryIn) & 0x7FFFFFFFFUL;
        vLo = (uint)(finalV & 0xFFFFFFFFUL);
        vHi = (uint)(finalV >> 32);
        tileCarry = h2 + warpScan.Generate;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackWord35To32(int bitPos, uint vLo, uint vHi)
    {
        int k = (bitPos * 937) >> 15;
        int off = bitPos - k * 35;
        int kNext = k + 1 < 32 ? k + 1 : 31;
        ulong a = Warp.ShuffleSync(0xFFFFFFFFu, vLo, k) | ((ulong)Warp.ShuffleSync(0xFFFFFFFFu, vHi, k) << 32);
        uint bLo = Warp.ShuffleSync(0xFFFFFFFFu, vLo, kNext);
        return off <= 3 ? (uint)(a >> off) : ((uint)(a >> off) | (bLo << (35 - off)));
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreTileWords(
        Ntt35ResolveCarriesContext ctx, int tileLinearIdx, int tx, uint vLo, uint vHi, uint tileCarry)
    {
        if (tx == 31)
        {
            ctx.BlockCarries[tileLinearIdx] = tileCarry;
        }

        int wordBase = tileLinearIdx * 35;
        uint outWord1 = PackWord35To32(tx << 5, vLo, vHi);
        if (wordBase + tx < ctx.MaxWordCount)
        {
            ctx.OutputBuffer[wordBase + tx] = outWord1;
        }

        int w2 = 32 + (tx < 3 ? tx : 2);
        uint outWord2 = PackWord35To32(w2 << 5, vLo, vHi);
        if (tx < 3 && wordBase + 32 + tx < ctx.MaxWordCount)
        {
            ctx.OutputBuffer[wordBase + 32 + tx] = outWord2;
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNtt35CarriesTiled32x32(Ntt35ResolveCarriesContext ctx)
    {
        int tid = ThreadIndex.X;
        int tx = tid & 31;
        int ty = tid >> 5;

        int tilesX = ctx.Grid.N2 >> 5;
        int tilesY = ctx.Grid.N1 >> 5;
        int totalTiles = tilesX * tilesY;
        int tilesXMask = tilesX - 1;
        int log2TilesX = 31 - (int)Gpu.CountLeadingZeros((uint)tilesX);

        var sLo = new SharedMemory<uint>(1120);
        var sMid = new SharedMemory<uint>(1120);
        var sHi = new SharedMemory<uint>(1120);

        for (int tileIdx = BlockIndex.X; tileIdx < totalTiles; tileIdx += GridDimension.X)
        {
            int bx = tileIdx & tilesXMask;
            int by = tileIdx >> log2TilesX;
            int r0 = bx << 5;
            int c0 = by << 5;

            LoadCrtTile(ctx, r0, c0, tx, ty, sLo, sMid, sHi);
            Gpu.SyncThreads();

            ResolveTileDigits(sLo, sMid, sHi, tx, ty, out uint vLo, out uint vHi, out uint tileCarry);
            int tileLinearIdx = (c0 + ty) * tilesX + bx;
            StoreTileWords(ctx, tileLinearIdx, tx, vLo, vHi, tileCarry);

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ResolveNtt35CarriesTiled32x32Kernel(Ntt35ResolveCarriesContext ctx)
    {
        ExecuteResolveNtt35CarriesTiled32x32(ctx);
    }
}

