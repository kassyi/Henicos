// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static partial class GpuBigIntVectorKernels
    {
        public static IntPtr VectorSignMagnitudeToTwosComplementKernelHandle;
        public static IntPtr VectorSignMagnitudeToOffsetBinaryKernelHandle;


    
    [GpuDeviceFunction]
    public static void DeviceVectorSignMagnitudeToTwosComplement(
        [GpuReadOnly] uint[] src,
        [GpuReadOnly] uint[] signSrc,
        uint[] dest,
        int numWords,
        int batchSize)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;

        if (bIdx >= batchSize) return;

        int srcOffset = bIdx * numWords;
        int destOffset = bIdx * numWords;
        int sign = (int)signSrc[bIdx];

        uint blockCarryIn = (uint)sign;
        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint val = valid ? src[srcOffset + idx] : 0u;
            if (sign == 1) val = ~val;

            ulong sum = (ulong)val;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            int laneId = tid % 32;
            int warpId = tid / 32;

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
                var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
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
                carryIn = GpuBigIntKernels.ApplyCarry(new GpuBigIntKernels.CarryState(prevScanG, prevScanP), carryIn);
            }

            uint resultVal = (uint)sum + carryIn;
            if (valid) dest[destOffset + idx] = resultVal;

            if (tid == bDim - 1)
            {
                blockCarryIn = GpuBigIntKernels.ApplyCarry(warpScan, carryIn);
            }
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void VectorSignMagnitudeToTwosComplementKernel(
        [GpuReadOnly] uint[] src,
        [GpuReadOnly] uint[] signSrc,
        uint[] dest,
        int numWords,
        int batchSize)
    {
        DeviceVectorSignMagnitudeToTwosComplement(src, signSrc, dest, numWords, batchSize);
    }

    [GpuKernel]
    public static void VectorSignMagnitudeToOffsetBinaryKernel(
        [GpuReadOnly] uint[] src,
        [GpuReadOnly] uint[] signSrc,
        uint[] dest,
        int numWords,
        int batchSize)
    {
        DeviceVectorSignMagnitudeToTwosComplement(src, signSrc, dest, numWords, batchSize);
        Gpu.SyncThreads();

        int tid = ThreadIndex.X;
        int bIdx = BlockIndex.X;
        if (bIdx >= batchSize) return;
        
        int destOffset = bIdx * numWords;
        
        if (tid == 0)
        {
            dest[destOffset + numWords - 1] ^= 0x80000000u;
        }
    }

    [GpuKernel]
    public static void VectorAddScalarKernel(uint[] dA, uint[] s, uint[] dResult, uint[] signA, uint scalarSign, uint[] signResult, int numWordsA, int numWordsS, int numWordsResult)
    {
        DeviceVectorAddSubParallel(dA, s, dResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, 0, scalarSign, true, true);
    }

    [GpuKernel]
    public static void VectorSubScalarKernel(uint[] dA, uint[] s, uint[] dResult, uint[] signA, uint scalarSign, uint[] signResult, int numWordsA, int numWordsS, int numWordsResult)
    {
        DeviceVectorAddSubParallel(dA, s, dResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, 1, scalarSign, true, true);
    }

    [GpuKernel]
    public static void VectorZipAddKernel(uint[] dA, uint[] dB, uint[] dResult, uint[] signA, uint[] signB, uint[] signResult, int numWordsA, int numWordsB, int numWordsResult)
    {
        DeviceVectorAddSubParallel(dA, dB, dResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 0, 0, false, true);
    }

    [GpuKernel]
    public static void VectorZipSubKernel(uint[] dA, uint[] dB, uint[] dResult, uint[] signA, uint[] signB, uint[] signResult, int numWordsA, int numWordsB, int numWordsResult)
    {
        DeviceVectorAddSubParallel(dA, dB, dResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 1, 0, false, true);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddSubParallel(
        [GpuReadOnly] uint[] a, 
        [GpuReadOnly] uint[] s, 
        uint[] result,          
        [GpuReadOnly] uint[] signA, 
        [GpuReadOnly] uint[] signB, 
        uint[] signResult,          
        int numWordsA,
        int numWordsS,
        int numWordsResult,
        uint opSign,
        uint scalarSign, 
        bool isScalar,
        bool syncThreads = true)
    {
        DeviceVectorAddSubAlignedCore(
            a, s, result,
            signA, signB, signResult,
            numWordsA, numWordsS, numWordsResult,
            opSign, scalarSign, isScalar,
            0, 0, syncThreads);
    }

    [GpuKernel]
    public static void VectorMulScalarKernel(uint[] dA, uint[] s, uint[] dResult, uint[] signA, uint scalarSign, uint[] signResult, int numWordsA, int numWordsS, int numWordsResult)
    {
        if (ThreadIndex.X == 0) signResult[BlockIndex.X] = signA[BlockIndex.X] ^ scalarSign;
        DeviceVectorMulVectorParallel(dA, s, dResult, numWordsA, numWordsS, numWordsResult, true, true);
    }

    [GpuKernel]
    public static void VectorZipMulKernel(uint[] dA, uint[] dB, uint[] dResult, uint[] signA, uint[] signB, uint[] signResult, int numWordsA, int numWordsB, int numWordsResult)
    {
        if (ThreadIndex.X == 0) signResult[BlockIndex.X] = signA[BlockIndex.X] ^ signB[BlockIndex.X];
        DeviceVectorMulVectorParallel(dA, dB, dResult, numWordsA, numWordsB, numWordsResult, false, true);
    }

    [GpuKernel]
    public static void VectorFloatMulScalarKernel(
        uint[] dA, uint[] s, uint[] dResult,
        uint[] expA, int scalarExp, uint[] expResult,
        uint[] signA, uint scalarSign, uint[] signResult,
        int numWordsA, int numWordsS, int numWordsResult)
    {
        int bIdx = BlockIndex.X;
        if (ThreadIndex.X == 0)
        {
            signResult[bIdx] = signA[bIdx] ^ scalarSign;
            expResult[bIdx] = (uint)((int)expA[bIdx] + scalarExp);
        }
        DeviceVectorMulVectorParallel(dA, s, dResult, numWordsA, numWordsS, numWordsResult, true, true);
    }

    [GpuKernel]
    public static void VectorFloatZipMulKernel(
        uint[] dA, uint[] dB, uint[] dResult,
        uint[] expA, uint[] expB, uint[] expResult,
        uint[] signA, uint[] signB, uint[] signResult,
        int numWordsA, int numWordsB, int numWordsResult)
    {
        int bIdx = BlockIndex.X;
        if (ThreadIndex.X == 0)
        {
            signResult[bIdx] = signA[bIdx] ^ signB[bIdx];
            expResult[bIdx] = (uint)((int)expA[bIdx] + (int)expB[bIdx]);
        }
        DeviceVectorMulVectorParallel(dA, dB, dResult, numWordsA, numWordsB, numWordsResult, false, true);
    }

    [GpuKernel]
    public static void VectorDivScalarKernel(uint[] dA, uint[] s, uint[] dResult, uint[] signA, uint scalarSign, uint[] signResult, int numWordsA, int numWordsS, int numWordsResult)
    {
        DeviceVectorDivModParallel(dA, s, dResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, scalarSign, true, false, 0);
    }

    [GpuKernel]
    public static void VectorModScalarKernel(uint[] dA, uint[] s, uint[] dResult, uint[] signA, uint scalarSign, uint[] signResult, int numWordsA, int numWordsS, int numWordsResult)
    {
        DeviceVectorDivModParallel(dA, s, dResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, scalarSign, true, true, 0);
    }

    [GpuKernel]
    public static void VectorZipDivKernel(uint[] dA, uint[] dB, uint[] dResult, uint[] signA, uint[] signB, uint[] signResult, int numWordsA, int numWordsB, int numWordsResult)
    {
        DeviceVectorDivModParallel(dA, dB, dResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 0u, false, false, 0);
    }

    [GpuKernel]
    public static void VectorZipModKernel(uint[] dA, uint[] dB, uint[] dResult, uint[] signA, uint[] signB, uint[] signResult, int numWordsA, int numWordsB, int numWordsResult)
    {
        DeviceVectorDivModParallel(dA, dB, dResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 0u, false, true, 0);
    }

    [GpuKernel]
    public static void VectorFloatDivScalarKernel(
        uint[] dA, uint[] s, uint[] dResult,
        uint[] expA, int scalarExp, uint[] expResult,
        uint[] signA, uint scalarSign, uint[] signResult,
        int numWordsA, int numWordsS, int numWordsResult,
        int preShiftBits)
    {
        DeviceVectorDivModParallel(dA, s, dResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, scalarSign, true, false, preShiftBits);
        if (ThreadIndex.X == 0)
        {
            int bIdx = BlockIndex.X;
            expResult[bIdx] = (uint)((int)expA[bIdx] - scalarExp - preShiftBits);
        }
    }

    [GpuKernel]
    public static void VectorFloatZipDivKernel(
        uint[] dA, uint[] dB, uint[] dResult,
        uint[] expA, uint[] expB, uint[] expResult,
        uint[] signA, uint[] signB, uint[] signResult,
        int numWordsA, int numWordsB, int numWordsResult,
        int preShiftBits)
    {
        DeviceVectorDivModParallel(dA, dB, dResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 0u, false, false, preShiftBits);
        if (ThreadIndex.X == 0)
        {
            int bIdx = BlockIndex.X;
            expResult[bIdx] = (uint)((int)expA[bIdx] - (int)expB[bIdx] - preShiftBits);
        }
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddSubAlignedCore(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] s,
        uint[] result,
        [GpuReadOnly] uint[] signA,
        [GpuReadOnly] uint[] signB,
        uint[] signResult,
        int numWordsA,
        int numWordsS,
        int numWordsResult,
        uint opSign,
        uint scalarSign,
        bool isScalar,
        int bitOffsetA,
        int bitOffsetB,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        uint s_sign = isScalar ? scalarSign : signB[batchIdx];
        uint effSignB = s_sign ^ opSign;
        bool differentSigns = (signA[batchIdx] != effSignB);

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = differentSigns ? 1u : 0u;
        int baseA = batchIdx * numWordsA;
        int baseB = (isScalar ? 0 : batchIdx) * numWordsS;

        for (int offset = 0; offset < numWordsResult; offset += bDim)
        {
            int idx = offset + tid;
            uint valA = idx < numWordsResult ? GetVectorShiftedWord(a, baseA, numWordsA, idx, bitOffsetA) : 0u;
            uint valB = idx < numWordsResult ? GetVectorShiftedWord(s, baseB, numWordsS, idx, bitOffsetB) : 0u;

            if (differentSigns)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            uint scanG = warpScan.Generate;
            uint scanP = warpScan.Propagate;

            if (laneId == 31) sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
            Gpu.SyncThreads();

            if (warpId == 0)
            {
                int numWarps = (bDim + 31) / 32;
                var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
                var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);
                if (laneId < numWarps) sharedCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
            }
            Gpu.SyncThreads();

            uint carryIn = blockCarryIn;
            if (warpId > 0) carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]), carryIn);

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
            if (laneId > 0) carryIn = prevScanG | (prevScanP & carryIn);

            if (idx < numWordsResult)
            {
                result[batchIdx * numWordsResult + idx] = (uint)sum + carryIn;
            }

            blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);
            Gpu.SyncThreads();
        }

        SharedMemory<uint> needsNegate = new SharedMemory<uint>(1);
        if (tid == 0)
        {
            if (!differentSigns)
            {
                signResult[batchIdx] = signA[batchIdx];
                needsNegate[0] = 0;
            }
            else
            {
                uint topWord = result[batchIdx * numWordsResult + numWordsResult - 1];
                bool isNegTC = (topWord & 0x80000000u) != 0;
                signResult[batchIdx] = isNegTC ? effSignB : signA[batchIdx];
                needsNegate[0] = isNegTC ? 1u : 0u;
            }
        }
        Gpu.SyncThreads();

        if (needsNegate[0] == 1u)
        {
            blockCarryIn = 1u;
            for (int offset = 0; offset < numWordsResult; offset += bDim)
            {
                int idx = offset + tid;
                uint valA = (idx < numWordsResult) ? ~result[batchIdx * numWordsResult + idx] : 0u;

                uint g = 0u;
                uint p = valA == 0xFFFFFFFFu ? 1u : 0u;

                var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
                uint scanG = warpScan.Generate;
                uint scanP = warpScan.Propagate;

                if (laneId == 31) sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
                Gpu.SyncThreads();

                if (warpId == 0)
                {
                    int numWarps = (bDim + 31) / 32;
                    var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
                    var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);
                    if (laneId < numWarps) sharedCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
                }
                Gpu.SyncThreads();

                uint carryIn = blockCarryIn;
                if (warpId > 0) carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]), carryIn);

                uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
                uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
                if (laneId > 0) carryIn = prevScanG | (prevScanP & carryIn);

                if (idx < numWordsResult)
                {
                    result[batchIdx * numWordsResult + idx] = valA + carryIn;
                }

                blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);
                Gpu.SyncThreads();
            }
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatAddSubParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] s,
        uint[] result,
        [GpuReadOnly] uint[] expA,
        [GpuReadOnly] uint[] expB,
        uint[] expResult,
        [GpuReadOnly] uint[] signA,
        [GpuReadOnly] uint[] signB,
        uint[] signResult,
        int numWordsA,
        int numWordsS,
        int numWordsResult,
        uint opSign,
        int scalarExp,
        uint scalarSign,
        bool isScalar,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int batchIdx = BlockIndex.X;

        int eA = (int)expA[batchIdx];
        int eB = isScalar ? scalarExp : (int)expB[batchIdx];

        int bitOffsetA = 0;
        int bitOffsetB = 0;
        int outExp = eA;

        if (eA > eB)
        {
            int diff = eA - eB;
            int headroomA = (numWordsResult - numWordsA) * 32;
            if (headroomA < 0) headroomA = 0;
            if (diff <= headroomA)
            {
                bitOffsetA = diff;
                outExp = eB;
            }
            else
            {
                bitOffsetB = -diff;
                outExp = eA;
            }
        }
        else if (eB > eA)
        {
            int diff = eB - eA;
            int headroomB = (numWordsResult - numWordsS) * 32;
            if (headroomB < 0) headroomB = 0;
            if (diff <= headroomB)
            {
                bitOffsetB = diff;
                outExp = eA;
            }
            else
            {
                bitOffsetA = -diff;
                outExp = eB;
            }
        }

        if (tid == 0)
        {
            expResult[batchIdx] = (uint)outExp;
        }

        DeviceVectorAddSubAlignedCore(
            a, s, result,
            signA, signB, signResult,
            numWordsA, numWordsS, numWordsResult,
            opSign, scalarSign, isScalar,
            bitOffsetA, bitOffsetB, syncThreads);
    }

    [GpuKernel]
    public static void VectorFloatAddScalarKernel(
        uint[] dA, uint[] s, uint[] dResult,
        uint[] expA, int scalarExp, uint[] expResult,
        uint[] signA, uint scalarSign, uint[] signResult,
        int numWordsA, int numWordsS, int numWordsResult)
    {
        DeviceVectorFloatAddSubParallel(dA, s, dResult, expA, expA, expResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, 0u, scalarExp, scalarSign, true, true);
    }

    [GpuKernel]
    public static void VectorFloatSubScalarKernel(
        uint[] dA, uint[] s, uint[] dResult,
        uint[] expA, int scalarExp, uint[] expResult,
        uint[] signA, uint scalarSign, uint[] signResult,
        int numWordsA, int numWordsS, int numWordsResult)
    {
        DeviceVectorFloatAddSubParallel(dA, s, dResult, expA, expA, expResult, signA, signA, signResult, numWordsA, numWordsS, numWordsResult, 1u, scalarExp, scalarSign, true, true);
    }

    [GpuKernel]
    public static void VectorFloatZipAddKernel(
        uint[] dA, uint[] dB, uint[] dResult,
        uint[] expA, uint[] expB, uint[] expResult,
        uint[] signA, uint[] signB, uint[] signResult,
        int numWordsA, int numWordsB, int numWordsResult)
    {
        DeviceVectorFloatAddSubParallel(dA, dB, dResult, expA, expB, expResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 0u, 0, 0u, false, true);
    }

    [GpuKernel]
    public static void VectorFloatZipSubKernel(
        uint[] dA, uint[] dB, uint[] dResult,
        uint[] expA, uint[] expB, uint[] expResult,
        uint[] signA, uint[] signB, uint[] signResult,
        int numWordsA, int numWordsB, int numWordsResult)
    {
        DeviceVectorFloatAddSubParallel(dA, dB, dResult, expA, expB, expResult, signA, signB, signResult, numWordsA, numWordsB, numWordsResult, 1u, 0, 0u, false, true);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorDivModParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        [GpuReadOnly] uint[] signA,
        [GpuReadOnly] uint[] signB,
        uint[] signResult,
        int numWordsA,
        int numWordsB,
        int numWordsResult,
        uint scalarSign,
        bool isScalar,
        bool isModulo,
        int preShiftBits = 0)
    {
        int tid = ThreadIndex.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid & 31;

        var sharedRem = new SharedMemory<uint>(128);
        var sharedQuot = new SharedMemory<uint>(128);

        if (tid >= 32)
        {
            return;
        }

        int offsetA = batchIdx * numWordsA;
        int offsetB = isScalar ? 0 : batchIdx * numWordsB;
        int offsetResult = batchIdx * numWordsResult;

        int maxInputWords = numWordsA > numWordsB ? numWordsA : numWordsB;
        int evalWords = numWordsResult > maxInputWords ? numWordsResult : maxInputWords;
        if (evalWords > 128)
        {
            evalWords = 128;
        }

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

        uint bAny = 0u;
        for (int offset = 0; offset < numWordsB; offset += 32)
        {
            int idx = offset + laneId;
            uint bVal = idx < numWordsB ? b[offsetB + idx] : 0u;
            bAny |= bVal;
        }

        for (uint delta = 16; delta > 0; delta >>= 1)
        {
            bAny |= Warp.ShuffleDownSync(0xFFFFFFFFu, bAny, delta);
        }

        bAny = Warp.ShuffleSync(0xFFFFFFFFu, bAny, 0);
        if (bAny == 0u)
        {
            if (laneId == 0)
            {
                signResult[batchIdx] = 0u;
            }

            return;
        }

        int maxNonZeroWordA = -1;
        for (int offset = 0; offset < numWordsA; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < numWordsA && a[offsetA + idx] != 0u)
            {
                maxNonZeroWordA = idx;
            }
        }

        for (uint delta = 16; delta > 0; delta >>= 1)
        {
            int other = Warp.ShuffleDownSync(0xFFFFFFFFu, maxNonZeroWordA, delta);
            if (other > maxNonZeroWordA)
            {
                maxNonZeroWordA = other;
            }
        }

        maxNonZeroWordA = Warp.ShuffleSync(0xFFFFFFFFu, maxNonZeroWordA, 0);
        if (maxNonZeroWordA < 0)
        {
            if (laneId == 0)
            {
                signResult[batchIdx] = 0u;
            }

            return;
        }

        for (int bit = (maxNonZeroWordA + 1) * 32 + preShiftBits - 1; bit >= 0; bit--)
        {
            int qWordIdx = bit >> 5;
            int qBitIdx = bit & 31;
            uint aBit = 0u;
            if (bit >= preShiftBits)
            {
                int srcBit = bit - preShiftBits;
                aBit = (a[offsetA + (srcBit >> 5)] >> (srcBit & 31)) & 1u;
            }

            uint chunkShiftIn = aBit;
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

            uint remOverflow = chunkShiftIn;

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
                uint laneCarryOut = GpuBigIntKernels.ApplyCarry(warpScan, warpCarryIn);
                warpCarryIn = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut, 31);
            }

            if (remOverflow != 0u || warpCarryIn != 0u)
            {
                uint subCarryIn = 1u;
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
                    uint prevG = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Generate, 1u);
                    uint prevP = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Propagate, 1u);

                    uint carryIn = subCarryIn;
                    if (laneId > 0)
                    {
                        carryIn = prevG | (prevP & carryIn);
                    }

                    if (valid)
                    {
                        sharedRem[idx] = (uint)sum + carryIn;
                    }

                    uint laneCarryOut = GpuBigIntKernels.ApplyCarry(warpScan, subCarryIn);
                    subCarryIn = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut, 31);
                }

                if (laneId == 0 && qWordIdx < evalWords)
                {
                    sharedQuot[qWordIdx] |= 1u << qBitIdx;
                }
            }
        }

        // Ensure sharedQuot writes from lane 0 are visible across the warp
        uint syncDummy = Warp.ShuffleSync(0xFFFFFFFFu, sharedQuot[0], 0);
        uint outAny = syncDummy & 0u;

        for (int offset = 0; offset < numWordsResult; offset += 32)
        {
            int idx = offset + laneId;
            if (idx < numWordsResult)
            {
                uint val = idx < evalWords ? (isModulo ? sharedRem[idx] : sharedQuot[idx]) : 0u;
                result[offsetResult + idx] = val;
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
            uint sA = signA[batchIdx];
            uint sB = isScalar ? scalarSign : signB[batchIdx];
            uint rawSign = isModulo ? sA : (sA ^ sB);
            signResult[batchIdx] = outAny != 0u ? rawSign : 0u;
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMulVectorParallel(
        [GpuReadOnly] uint[] a, 
        [GpuReadOnly] uint[] b, 
        uint[] result,
        int numWordsA,
        int numWordsB,
        int numWordsResult,
        bool isScalar,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        int offsetA = batchIdx * numWordsA;
        int offsetB = isScalar ? 0 : batchIdx * numWordsB;
        int offsetResult = batchIdx * numWordsResult;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

        for (int offset = 0; offset < numWordsResult; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < numWordsResult)
            {
                result[offsetResult + idx] = 0u;
            }
        }
        Gpu.SyncThreads();

        for (int i = 0; i < numWordsB; i++)
        {
            uint bVal = b[offsetB + i];
            if (bVal == 0) continue;

            uint blockCarryIn = 0u;
            uint blockHIn = 0u;

            for (int offset = 0; offset < numWordsA; offset += bDim)
            {
                int j = offset + tid;
                bool valid = j < numWordsA && (i + j < numWordsResult); 

                uint aVal = valid ? a[offsetA + j] : 0u;
                ulong prod = (ulong)aVal * bVal;
                uint L = (uint)(prod & 0xFFFFFFFFul);
                uint H = (uint)(prod >> 32);

                uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, H, 1);
                if (laneId == 0)
                {
                    prevH = blockHIn;
                }

                ulong sum1 = (ulong)L + prevH;
                uint currentResult = valid ? result[offsetResult + i + j] : 0u;
                ulong sum2 = sum1 + currentResult;

                uint finalL = (uint)(sum2 & 0xFFFFFFFFul);
                uint g = (uint)(sum2 >> 32);
                uint p = (uint)sum2 == 0xFFFFFFFFu ? 1u : 0u;

                var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
                uint scanG = warpScan.Generate;
                uint scanP = warpScan.Propagate;

                if (laneId == 31)
                {
                    sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
                    sharedH[warpId] = H;
                }

                Gpu.SyncThreads();

                if (warpId == 0)
                {
                    int numWarps = (bDim + 31) / 32;
                    var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
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

                if (valid)
                {
                    result[offsetResult + i + j] = finalL + carryIn;
                }

                blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);
                blockHIn = sharedH[((bDim + 31) / 32) - 1];

                Gpu.SyncThreads();
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMulScalarParallel(
        [GpuReadOnly] uint[] a, // Length: numWords * batchSize
        [GpuReadOnly] uint[] s, // Length: numWords
        uint[] result,
        int numWords,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;
        int elementOffset = batchIdx * numWords;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

        // Clear result buffer for this batch element
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < numWords)
            {
                result[elementOffset + idx] = 0u;
            }
        }
        Gpu.SyncThreads();

        // O(N^2) multiplication using GridStrideLoop equivalent
        for (int i = 0; i < numWords; i++)
        {
            uint sVal = s[i];
            if (sVal == 0) continue;

            uint blockCarryIn = 0u;
            uint blockHIn = 0u;

            for (int offset = 0; offset < numWords; offset += bDim)
            {
                int j = offset + tid;
                bool valid = j < (numWords - i); // Only compute valid parts that fit

                uint aVal = valid ? a[elementOffset + j] : 0u;
                ulong prod = (ulong)aVal * sVal;
                uint L = (uint)(prod & 0xFFFFFFFFul);
                uint H = (uint)(prod >> 32);

                uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, H, 1);
                if (laneId == 0)
                {
                    prevH = blockHIn;
                }

                ulong sum1 = (ulong)L + prevH;
                uint currentResult = valid ? result[elementOffset + i + j] : 0u;
                ulong sum2 = sum1 + currentResult;

                uint finalL = (uint)(sum2 & 0xFFFFFFFFul);
                uint g = (uint)(sum2 >> 32);
                uint p = (uint)sum2 == 0xFFFFFFFFu ? 1u : 0u;

                var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
                uint scanG = warpScan.Generate;
                uint scanP = warpScan.Propagate;

                if (laneId == 31)
                {
                    sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
                    sharedH[warpId] = H;
                }

                Gpu.SyncThreads();

                if (warpId == 0)
                {
                    int numWarps = (bDim + 31) / 32;
                    var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
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

                if (valid)
                {
                    result[elementOffset + i + j] = finalL + carryIn;
                }

                blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);
                blockHIn = sharedH[((bDim + 31) / 32) - 1];

                Gpu.SyncThreads();
            }
        }
    }

[GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddSubVectorParallel(
        [GpuReadOnly] uint[] a, 
        [GpuReadOnly] uint[] b, 
        uint[] result,
        int numWordsA,
        int numWordsB,
        int numWordsResult,
        int sign,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;
        
        int offsetA = batchIdx * numWordsA;
        int offsetB = batchIdx * numWordsB;
        int offsetResult = batchIdx * numWordsResult;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        uint blockCarryIn = sign == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWordsResult; offset += bDim)
        {
            int idx = offset + tid;
            bool validA = idx < numWordsA;
            bool validB = idx < numWordsB;
            bool validResult = idx < numWordsResult;

            uint valA = validA ? a[offsetA + idx] : 0u;
            uint valB = validB ? b[offsetB + idx] : 0u;

            if (sign == -1)
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
                var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
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

            if (validResult)
            {
                result[offsetResult + idx] = (uint)sum + carryIn;
            }

            blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);

            Gpu.SyncThreads();
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void VectorSumReductionChunkKernel(uint[] dA, uint[] dResult, int numWords, int batchSize, int chunkSize)
    {
        DeviceVectorSumReductionChunk(dA, dResult, numWords, batchSize, chunkSize);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorSumReductionChunk(
        [GpuReadOnly] uint[] a, // Length: numWords * batchSize
        uint[] result,          // Length: numWords * GridDim.X
        int numWords,
        int batchSize,
        int chunkSize)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * numWords;

        int startIndex = bIdx * chunkSize;
        int endIndex = startIndex + chunkSize;
        if (endIndex > batchSize)
        {
            endIndex = batchSize;
        }

        // Clear result buffer for this block
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < numWords)
            {
                result[resultOffset + idx] = 0u;
            }
        }
        Gpu.SyncThreads();

        // Loop over the chunk and accumulate into the result buffer
        for (int i = startIndex; i < endIndex; i++)
        {
            DeviceVectorAddAccumulateParallel(a, i * numWords, result, resultOffset, numWords);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddAccumulateParallel(
        [GpuReadOnly] uint[] src, 
        int srcOffset,
        uint[] dest,
        int destOffset,
        int numWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? src[srcOffset + idx] : 0u;
            uint valB = valid ? dest[destOffset + idx] : 0u;

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
                var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
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

            if (valid)
            {
                dest[destOffset + idx] = (uint)sum + carryIn;
            }

            blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void VectorMaxReductionChunkKernel(uint[] dA, uint[] dResult, int numWords, int batchSize, int chunkSize)
    {
        DeviceVectorMaxReductionChunk(dA, dResult, numWords, batchSize, chunkSize);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMaxReductionChunk(
        [GpuReadOnly] uint[] a, 
        uint[] result,
        int numWords,
        int batchSize,
        int chunkSize)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * numWords;
        int startIndex = bIdx * chunkSize;
        int endIndex = startIndex + chunkSize;
        if (endIndex > batchSize) endIndex = batchSize;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < numWords) result[resultOffset + idx] = 0u;
        }
        Gpu.SyncThreads();

        for (int i = startIndex; i < endIndex; i++)
        {
            DeviceVectorMaxAccumulateParallel(a, i * numWords, result, resultOffset, numWords);
        }
    }

    [GpuKernel]
    public static void VectorMinReductionChunkKernel(uint[] dA, uint[] dResult, int numWords, int batchSize, int chunkSize)
    {
        DeviceVectorMinReductionChunk(dA, dResult, numWords, batchSize, chunkSize);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMinReductionChunk(
        [GpuReadOnly] uint[] a, 
        uint[] result,
        int numWords,
        int batchSize,
        int chunkSize)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * numWords;
        int startIndex = bIdx * chunkSize;
        int endIndex = startIndex + chunkSize;
        if (endIndex > batchSize) endIndex = batchSize;

        if (startIndex < batchSize)
        {
            for (int offset = 0; offset < numWords; offset += bDim)
            {
                int idx = offset + tid;
                if (idx < numWords) result[resultOffset + idx] = a[startIndex * numWords + idx];
            }
        }
        Gpu.SyncThreads();

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            DeviceVectorMinAccumulateParallel(a, i * numWords, result, resultOffset, numWords);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMaxAccumulateParallel([GpuReadOnly] uint[] src, int srcOffset, uint[] dest, int destOffset, int numWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        ulong myVal = 0;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;
            uint ws = valid ? src[srcOffset + idx] : 0u;
            uint wd = valid ? dest[destOffset + idx] : 0u;

            if (valid && ws != wd)
            {
                long diff = (long)ws - (long)wd;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        uint mask = Warp.ActiveMask();
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(myVal >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)myVal, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (otherVal > myVal) myVal = otherVal;
        }

        if (laneId == 0) warpMax[warpId] = myVal;
        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            ulong val = laneId < numWarps ? warpMax[laneId] : 0ul;

            uint activeMask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(activeMask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(activeMask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val) val = otherVal;
            }

            if (laneId == 0) warpMax[0] = val;
        }
        Gpu.SyncThreads();

        if ((warpMax[0] & 0xFFFFFFFFul) == 2ul)
        {
            for (int offset = 0; offset < numWords; offset += bDim)
            {
                int idx = offset + tid;
                if (idx < numWords) dest[destOffset + idx] = src[srcOffset + idx];
            }
        }
        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMinAccumulateParallel([GpuReadOnly] uint[] src, int srcOffset, uint[] dest, int destOffset, int numWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        ulong myVal = 0;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;
            uint ws = valid ? src[srcOffset + idx] : 0u;
            uint wd = valid ? dest[destOffset + idx] : 0u;

            if (valid && ws != wd)
            {
                long diff = (long)ws - (long)wd;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        uint mask = Warp.ActiveMask();
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(myVal >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)myVal, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (otherVal > myVal) myVal = otherVal;
        }

        if (laneId == 0) warpMax[warpId] = myVal;
        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            ulong val = laneId < numWarps ? warpMax[laneId] : 0ul;

            uint activeMask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(activeMask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(activeMask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val) val = otherVal;
            }

            if (laneId == 0) warpMax[0] = val;
        }
        Gpu.SyncThreads();

        if ((warpMax[0] & 0xFFFFFFFFul) == 1ul) // 1ul means src < dest
        {
            for (int offset = 0; offset < numWords; offset += bDim)
            {
                int idx = offset + tid;
                if (idx < numWords) dest[destOffset + idx] = src[srcOffset + idx];
            }
        }
        Gpu.SyncThreads();
    }
}
