// ReSharper disable All

#pragma warning disable IDE0051, IDE0022, GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorDivScalarKernel(VectorDivModContext ctx)
    {
        ctx.SignB = ctx.SignA;
        ctx.IsScalar = true;
        ctx.IsModulo = false;
        ctx.PreShiftBits = 0;
        DeviceVectorDivModParallel(ctx);
    }

    [GpuKernel]
    public static void VectorModScalarKernel(VectorDivModContext ctx)
    {
        ctx.SignB = ctx.SignA;
        ctx.IsScalar = true;
        ctx.IsModulo = true;
        ctx.PreShiftBits = 0;
        DeviceVectorDivModParallel(ctx);
    }

    [GpuKernel]
    public static void VectorZipDivKernel(VectorDivModContext ctx)
    {
        ctx.ScalarSign = 0u;
        ctx.IsScalar = false;
        ctx.IsModulo = false;
        ctx.PreShiftBits = 0;
        DeviceVectorDivModParallel(ctx);
    }

    [GpuKernel]
    public static void VectorZipModKernel(VectorDivModContext ctx)
    {
        ctx.ScalarSign = 0u;
        ctx.IsScalar = false;
        ctx.IsModulo = true;
        ctx.PreShiftBits = 0;
        DeviceVectorDivModParallel(ctx);
    }

    [GpuKernel]
    public static void VectorFloatDivScalarKernel(VectorFloatDivContext ctx)
    {
        ctx.IsScalar = true;
        ctx.SignB = ctx.SignA;
        DeviceVectorFloatDivParallel(ctx);
    }

    [GpuKernel]
    public static void VectorFloatZipDivKernel(VectorFloatDivContext ctx)
    {
        ctx.IsScalar = false;
        ctx.ScalarSign = 0u;
        DeviceVectorFloatDivParallel(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatDivParallel(VectorFloatDivContext ctx)
    {
        var divCtx = new VectorDivModContext
        {
            A = ctx.A,
            B = ctx.B,
            Result = ctx.Result,
            SignA = ctx.SignA,
            SignB = ctx.SignB,
            SignResult = ctx.SignResult,
            NumWordsA = ctx.NumWordsA,
            NumWordsB = ctx.NumWordsB,
            NumWordsResult = ctx.NumWordsResult,
            ScalarSign = ctx.ScalarSign,
            IsScalar = ctx.IsScalar,
            IsModulo = false,
            PreShiftBits = ctx.PreShiftBits
        };
        DeviceVectorDivModParallel(divCtx);
        if (ThreadIndex.X == 0)
        {
            int bIdx = BlockIndex.X;
            int eB = ctx.IsScalar ? ctx.ScalarExp : (int)ctx.ExpB[bIdx];
            ctx.ExpResult[bIdx] = (uint)((int)ctx.ExpA[bIdx] - eB - ctx.PreShiftBits);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorDivModParallel(VectorDivModContext ctx)
    {
        int tid = ThreadIndex.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid & 31;

        var sharedRem = new SharedMemory<uint>(512);
        var sharedQuot = new SharedMemory<uint>(512);

        if (tid >= 32)
        {
            return;
        }

        int offsetA = batchIdx * ctx.NumWordsA;
        int offsetB = ctx.IsScalar ? 0 : batchIdx * ctx.NumWordsB;
        int offsetResult = batchIdx * ctx.NumWordsResult;

        int maxInputWords = ctx.NumWordsA > ctx.NumWordsB ? ctx.NumWordsA : ctx.NumWordsB;
        int evalWords = ctx.NumWordsResult > maxInputWords ? ctx.NumWordsResult : maxInputWords;
        if (evalWords > 512)
        {
            evalWords = 512;
        }

        InitializeDivModBuffersWarp(ctx.Result, offsetResult, ctx.NumWordsResult, sharedRem, sharedQuot, evalWords, laneId);

        if (FindMaxNonZeroWordWarp(ctx.B, offsetB, ctx.NumWordsB, laneId) < 0)
        {
            if (laneId == 0) ctx.SignResult[batchIdx] = 0u;
            return;
        }

        int maxNonZeroWordA = FindMaxNonZeroWordWarp(ctx.A, offsetA, ctx.NumWordsA, laneId);
        if (maxNonZeroWordA < 0)
        {
            if (laneId == 0) ctx.SignResult[batchIdx] = 0u;
            return;
        }

        int totalBits = (maxNonZeroWordA + 1) * 32 + ctx.PreShiftBits;
        for (int bit = totalBits - 1; bit >= 0; bit--)
        {
            uint aBit = 0u;
            if (bit >= ctx.PreShiftBits)
            {
                int srcBit = bit - ctx.PreShiftBits;
                aBit = (ctx.A[offsetA + (srcBit >> 5)] >> (srcBit & 31)) & 1u;
            }

            uint remOverflow = ShiftLeftOneBitWarp(sharedRem, evalWords, laneId, aBit);
            uint noBorrow = SubtractDivisorWarp(ctx.B, offsetB, ctx.NumWordsB, sharedRem, evalWords, laneId, false);

            if (remOverflow != 0u || noBorrow != 0u)
            {
                SubtractDivisorWarp(ctx.B, offsetB, ctx.NumWordsB, sharedRem, evalWords, laneId, true);

                int qWordIdx = bit >> 5;
                int qBitIdx = bit & 31;
                if (laneId == 0 && qWordIdx < evalWords)
                {
                    sharedQuot[qWordIdx] |= 1u << qBitIdx;
                }
            }
        }

        CommitDivModResultWarp(ctx, sharedQuot, sharedRem, evalWords, batchIdx, offsetResult, laneId);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InitializeDivModBuffersWarp(
        uint[] result,
        int offsetResult,
        int numWordsResult,
        SharedMemory<uint> sharedRem,
        SharedMemory<uint> sharedQuot,
        int evalWords,
        int laneId)
    {
        for (int offset = 0; offset < evalWords; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < evalWords)
            {
                sharedRem[idx] = 0u;
                sharedQuot[idx] = 0u;
            }
        }

        for (int offset = 0; offset < numWordsResult; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < numWordsResult)
            {
                result[offsetResult + idx] = 0u;
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindMaxNonZeroWordWarp(
        [GpuReadOnly] uint[] data,
        int baseOffset,
        int numWords,
        int laneId)
    {
        int maxNonZeroWord = -1;
        for (int offset = 0; offset < numWords; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < numWords && data[baseOffset + idx] != 0u)
            {
                maxNonZeroWord = idx;
            }
        }

        for (uint delta = 16; delta > 0; delta >>= 1)
        {
            int other = Warp.ShuffleDownSync(0xFFFFFFFFu, maxNonZeroWord, delta);
            if (other > maxNonZeroWord)
            {
                maxNonZeroWord = other;
            }
        }

        return Warp.ShuffleSync(0xFFFFFFFFu, maxNonZeroWord, 0);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ShiftLeftOneBitWarp(
        SharedMemory<uint> sharedRem,
        int evalWords,
        int laneId,
        uint shiftInBit)
    {
        uint chunkShiftIn = shiftInBit;
        for (int offset = 0; offset < evalWords; offset += 32)
        {
            int idx = offset + laneId;
            bool valid = idx < evalWords;

            uint rVal = valid ? sharedRem[idx] : 0u;
            uint topBit = rVal >> 31;
            uint prevTopBit = Warp.ShuffleUpSync(0xFFFFFFFFu, topBit, 1u);
            if (laneId == 0)
            {
                prevTopBit = chunkShiftIn;
            }

            if (valid)
            {
                sharedRem[idx] = (rVal << 1) | prevTopBit;
            }

            int lastLaneInChunk = (offset + 32 <= evalWords) ? 31 : ((evalWords - 1) & 31);
            chunkShiftIn = Warp.ShuffleSync(0xFFFFFFFFu, topBit, lastLaneInChunk);
        }

        return chunkShiftIn;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SubtractDivisorWarp(
        [GpuReadOnly] uint[] b,
        int offsetB,
        int numWordsB,
        SharedMemory<uint> sharedRem,
        int evalWords,
        int laneId,
        bool commit)
    {
        uint warpCarryIn = 1u;
        for (int offset = 0; offset < evalWords; offset += 32)
        {
            int idx = offset + laneId;
            bool valid = idx < evalWords;

            uint rVal = valid ? sharedRem[idx] : 0u;
            uint bVal = (valid && idx < numWordsB) ? ~b[offsetB + idx] : 0xFFFFFFFFu;

            ulong sum = (ulong)rVal + bVal;
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);

            if (commit)
            {
                uint prevG = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Generate, 1u);
                uint prevP = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Propagate, 1u);

                uint carryIn = warpCarryIn;
                if (laneId > 0)
                {
                    carryIn = prevG | (prevP & carryIn);
                }

                if (valid)
                {
                    sharedRem[idx] = (uint)sum + carryIn;
                }
            }

            uint laneCarryOut = GpuBigIntKernels.ApplyCarry(warpScan, warpCarryIn);
            warpCarryIn = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut, 31);
        }

        return warpCarryIn;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CommitDivModResultWarp(
        VectorDivModContext ctx,
        SharedMemory<uint> sharedQuot,
        SharedMemory<uint> sharedRem,
        int evalWords,
        int batchIdx,
        int offsetResult,
        int laneId)
    {
        // Ensure sharedQuot writes from lane 0 are visible across the warp
        uint syncDummy = Warp.ShuffleSync(0xFFFFFFFFu, sharedQuot[0], 0);
        uint outAny = syncDummy & 0u;

        for (int offset = 0; offset < ctx.NumWordsResult; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < ctx.NumWordsResult)
            {
                uint val = idx < evalWords ? (ctx.IsModulo ? sharedRem[idx] : sharedQuot[idx]) : 0u;
                ctx.Result[offsetResult + idx] = val;
                outAny |= val;
            }
        }

        for (uint delta = 16; delta > 0; delta >>= 1)
        {
            outAny |= Warp.ShuffleDownSync(0xFFFFFFFFu, outAny, delta);
        }

        outAny = Warp.ShuffleSync(0xFFFFFFFFu, outAny, 0);
        if (laneId == 0)
        {
            uint sA = ctx.SignA[batchIdx];
            uint sB = ctx.IsScalar ? ctx.ScalarSign : ctx.SignB[batchIdx];
            uint rawSign = ctx.IsModulo ? sA : (sA ^ sB);
            ctx.SignResult[batchIdx] = outAny != 0u ? rawSign : 0u;
        }
    }
}
