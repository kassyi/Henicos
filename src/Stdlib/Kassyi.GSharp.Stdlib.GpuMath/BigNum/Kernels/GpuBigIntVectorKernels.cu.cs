// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorAddScalarKernel(uint[] dA, uint[] s, uint[] dResult, int numWords)
    {
        DeviceVectorAddSubParallel(dA, s, dResult, numWords, 1, true);
    }

    [GpuKernel]
    public static void VectorSubScalarKernel(uint[] dA, uint[] s, uint[] dResult, int numWords)
    {
        DeviceVectorAddSubParallel(dA, s, dResult, numWords, -1, true);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddSubParallel(
        [GpuReadOnly] uint[] a, // Length: numWords * batchSize
        [GpuReadOnly] uint[] s, // Length: numWords (scalar broadcast)
        uint[] result,          // Length: numWords * batchSize
        int numWords,
        int sign,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;
        int elementOffset = batchIdx * numWords;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        uint blockCarryIn = sign == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? a[elementOffset + idx] : 0u;
            uint valB = valid ? s[idx] : 0u;

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

            if (valid)
            {
                result[elementOffset + idx] = (uint)sum + carryIn;
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
    public static void VectorMulScalarKernel(uint[] dA, uint[] s, uint[] dResult, int numWords)
    {
        DeviceVectorMulScalarParallel(dA, s, dResult, numWords, true);
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
}
