namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using System.Runtime.CompilerServices;
using Core;

// Reference/Inspiration: stillwater-sc/universal (Mathematical concepts)

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigFloatKernels
{
    public struct GpuBigFloatAddContext
    {
        [GpuReadOnly]
        public uint[] LargerSig;

        public int LargerWords;

        [GpuReadOnly]
        public uint[] SmallerSig;

        public int SmallerWords;
        public uint[] ResultSig;
        public int NumWords;
        public int WordShift;
        public int BitShift;
        public int SignScale;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetShiftedRightWord([GpuReadOnly] uint[] data, int numWords, int destIdx, int wordShift,
        int bitShift)
    {
        var s0 = destIdx + wordShift;
        var s1 = s0 + 1;

        var w0 = (s0 >= 0 && s0 < numWords) ? data[s0] : 0u;
        var w1 = (s1 >= 0 && s1 < numWords) ? data[s1] : 0u;

        if (bitShift == 0)
        {
            return w0;
        }

        return (w0 >> bitShift) | (w1 << (32 - bitShift));
    }

    [GpuKernel]
    public static void FloatAddAlignedKernel(GpuBigFloatAddContext ctx)
    {
        var tid = ThreadIndex.X;
        var bDim = BlockDimension.X;
        var laneId = tid % 32;
        var warpId = tid / 32;

        var sharedCarry = new SharedMemory<uint>(32);

        var blockCarryIn = ctx.SignScale == 1 ? 0u : 1u;

        for (int offset = 0; offset < ctx.NumWords; offset += bDim)
        {
            var idx = offset + tid;
            var valid = idx < ctx.NumWords;

            var valA = (valid && idx < ctx.LargerWords) ? ctx.LargerSig[idx] : 0u;
            var valB = 0u;
            if (valid)
            {
                valB = GetShiftedRightWord(ctx.SmallerSig, ctx.SmallerWords, idx, ctx.WordShift, ctx.BitShift);
            }

            if (ctx.SignScale == -1)
            {
                valB = ~valB;
            }

            var sum = (ulong)valA + valB;
            var g = (uint)(sum >> 32);
            var p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            var scanG = warpScan.Generate;
            var scanP = warpScan.Propagate;

            if (laneId == 31)
            {
                sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
            }

            Gpu.SyncThreads();

            if (warpId == 0)
            {
                var numWarps = (bDim + 31) / 32;
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

            var carryIn = blockCarryIn;

            if (warpId > 0)
            {
                carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]),
                    carryIn);
            }

            var prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
            var prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                ctx.ResultSig[idx] = (uint)sum + carryIn;
            }

            blockCarryIn =
                GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]),
                    blockCarryIn);

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void FloatNormalizeKernel(
        [GpuReadOnly] uint[] valueSig, int valueWords,
        uint[] resultSig, int resultWords,
        int wordShift, int bitShift)
    {
        var tid = ThreadIndex.X;
        var bDim = BlockDimension.X;

        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            var idx = offset + tid;
            var valid = idx < resultWords;

            if (valid)
            {
                var srcIdxHi = idx - wordShift;
                var srcIdxLo = srcIdxHi - 1;

                var hi = (srcIdxHi >= 0 && srcIdxHi < valueWords) ? valueSig[srcIdxHi] : 0u;
                var lo = (srcIdxLo >= 0 && srcIdxLo < valueWords) ? valueSig[srcIdxLo] : 0u;

                if (bitShift == 0)
                {
                    resultSig[idx] = hi;
                }
                else
                {
                    resultSig[idx] = Gpu.FunnelShiftLeft(lo, hi, (uint)bitShift);
                }
            }
        }
    }
}
