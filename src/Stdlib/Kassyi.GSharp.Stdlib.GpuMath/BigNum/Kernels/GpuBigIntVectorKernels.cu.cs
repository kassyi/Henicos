#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntVectorKernels
{
    public static IntPtr VectorSignMagnitudeToTwosComplementKernelHandle;
    public static IntPtr VectorSignMagnitudeToOffsetBinaryKernelHandle;
    /// <summary>
    /// Performs a two-tier (warp + block) Kogge-Stone carry-lookahead scan across the thread block
    /// and returns the carry-in for the calling thread while outputting the block-wide carry-out.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PropagateBlockCarry(
        uint g,
        uint p,
        uint blockCarryIn,
        SharedMemory<uint> sharedCarry,
        int laneId,
        int warpId,
        int bDim,
        out uint nextBlockCarryIn)
    {
        var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
        uint scanG = warpScan.Generate;
        uint scanP = warpScan.Propagate;

        if (laneId == 31)
        {
            sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
        }

        Gpu.SyncThreads();

        int numWarps = (bDim + 31) / 32;
        if (warpId == 0)
        {
            var valState = laneId < numWarps
                ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId])
                : new GpuBigIntKernels.CarryState(0, 0);
            var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);

            if (laneId < numWarps)
            {
                sharedCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
            }
        }

        Gpu.SyncThreads();

        uint carryIn = blockCarryIn;
        if (warpId > 0)
        {
            carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]), carryIn);
        }

        uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
        uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
        if (laneId > 0)
        {
            carryIn = prevScanG | (prevScanP & carryIn);
        }

        nextBlockCarryIn = GpuBigIntKernels.ApplyCarry(
            GpuBigIntKernels.UnpackCarryState(sharedCarry[numWarps - 1]),
            blockCarryIn);

        return carryIn;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetVectorShiftedWord(
        [GpuReadOnly] uint[] data,
        int baseOffset,
        int numWords,
        int destIdx,
        int bitOffset)
    {
        if (bitOffset == 0)
        {
            return (destIdx >= 0 && destIdx < numWords) ? data[baseOffset + destIdx] : 0u;
        }

        if (bitOffset > 0)
        {
            int wordShift = bitOffset >> 5;
            int bitShift = bitOffset & 31;
            int s0 = destIdx - wordShift;
            int s1 = s0 - 1;
            uint w0 = (s0 >= 0 && s0 < numWords) ? data[baseOffset + s0] : 0u;
            uint w1 = (s1 >= 0 && s1 < numWords) ? data[baseOffset + s1] : 0u;
            return bitShift == 0 ? w0 : ((w0 << bitShift) | (w1 >> (32 - bitShift)));
        }
        else
        {
            int rightBits = -bitOffset;
            int wordShift = rightBits >> 5;
            int bitShift = rightBits & 31;
            int s0 = destIdx + wordShift;
            int s1 = s0 + 1;
            uint w0 = (s0 >= 0 && s0 < numWords) ? data[baseOffset + s0] : 0u;
            uint w1 = (s1 >= 0 && s1 < numWords) ? data[baseOffset + s1] : 0u;
            return bitShift == 0 ? w0 : ((w0 >> bitShift) | (w1 << (32 - bitShift)));
        }
    }

    [GpuDeviceFunction]
    public static void DeviceVectorSignMagnitudeToTwosComplement(VectorSignConvertContext ctx)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;

        if (bIdx >= ctx.BatchSize)
        {
            return;
        }

        int numWords = ctx.Source.NumWords;
        int srcOffset = bIdx * numWords;
        int destOffset = bIdx * numWords;
        int sign = (int)ctx.Source.Signs[bIdx];
        int laneId = tid % 32;
        int warpId = tid / 32;

        uint blockCarryIn = (uint)sign;
        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint val = valid ? ctx.Source.Limbs[srcOffset + idx] : 0u;
            if (sign == 1)
            {
                val = ~val;
            }

            uint g = 0u;
            uint p = val == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            if (valid)
            {
                ctx.Dest[destOffset + idx] = val + carryIn;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void VectorSignMagnitudeToTwosComplementKernel(VectorSignConvertContext ctx)
    {
        DeviceVectorSignMagnitudeToTwosComplement(ctx);
    }

    [GpuKernel]
    public static void VectorSignMagnitudeToOffsetBinaryKernel(VectorSignConvertContext ctx)
    {
        DeviceVectorSignMagnitudeToTwosComplement(ctx);
        Gpu.SyncThreads();

        int tid = ThreadIndex.X;
        int bIdx = BlockIndex.X;
        if (bIdx >= ctx.BatchSize)
        {
            return;
        }

        int numWords = ctx.Source.NumWords;
        int destOffset = bIdx * numWords;

        if (tid == 0)
        {
            ctx.Dest[destOffset + numWords - 1] ^= 0x80000000u;
        }
    }
}
