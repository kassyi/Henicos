namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Kernels;

using System.Runtime.CompilerServices;
using Core;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Reference/Inspiration: stillwater-sc/universal (Mathematical concepts)

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigFloatKernels
{
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetShiftedRightWord([GpuReadOnly] uint[] data, int numWords, int destIdx, int wordShift,
        int bitShift)
    {
        int srcIdx0 = destIdx - wordShift;
        int
            srcIdx1 = srcIdx0 +
                      1; // wait, if we shift right, the bits come from the higher index? No, if we shift a BigInt RIGHT, it means dividing by 2. So the bits move to lower indices.

        // Wait! Let's clarify:
        // A BigInt is stored in little-endian. data[0] is the least significant word.
        // If we shift RIGHT (divide by 2), the bits from data[1] move into data[0].
        // So destIdx receives bits from destIdx + wordShift and destIdx + wordShift + 1.
        int s0 = destIdx + wordShift;
        int s1 = s0 + 1;

        uint w0 = (s0 >= 0 && s0 < numWords) ? data[s0] : 0u;
        uint w1 = (s1 >= 0 && s1 < numWords) ? data[s1] : 0u;

        if (bitShift == 0)
        {
            return w0;
        }

        return (w0 >> bitShift) | (w1 << (32 - bitShift));
    }

    [GpuKernel]
    public static void FloatAddAlignedKernel(
        [GpuReadOnly] uint[] largerSig, int largerWords,
        [GpuReadOnly] uint[] smallerSig, int smallerWords,
        uint[] resultSig, int numWords,
        int wordShift, int bitShift, int signScale)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        uint blockCarryIn = signScale == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = (valid && idx < largerWords) ? largerSig[idx] : 0u;
            uint valB = 0u;
            if (valid)
            {
                valB = GetShiftedRightWord(smallerSig, smallerWords, idx, wordShift, bitShift);
            }

            if (signScale == -1)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            uint scanG = warpScan.Generate;
            uint scanP = warpScan.Propagate;

            if (laneId == 31)
            {
                sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
            }

            Gpu.SyncThreads();

            if (warpId == 0)
            {
                int numWarps = (bDim + 31) / 32;
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
                carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]),
                    carryIn);
            }

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                resultSig[idx] = (uint)sum + carryIn;
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
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;

        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < resultWords;

            if (valid)
            {
                int srcIdxHi = idx - wordShift;
                int srcIdxLo = srcIdxHi - 1;

                uint hi = (srcIdxHi >= 0 && srcIdxHi < valueWords) ? valueSig[srcIdxHi] : 0u;
                uint lo = (srcIdxLo >= 0 && srcIdxLo < valueWords) ? valueSig[srcIdxLo] : 0u;

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
