// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntScalarKernels
{
    [GpuKernel]
    public static void ScalarAddKernel(uint[] dA, uint s, uint[] dResult, int numWords)
    {
        Device_ScalarAddSubParallel(dA, s, dResult, numWords, 1, true);
    }

    [GpuKernel]
    public static void ScalarSubKernel(uint[] dA, uint s, uint[] dResult, int numWords)
    {
        Device_ScalarAddSubParallel(dA, s, dResult, numWords, -1, true);
    }

    /// <summary>
    /// <c>dResult = dA + s</c> on <paramref name="numWords"/> words, like <see cref="ScalarAddKernel"/>, and records an
    /// overflow in the device status words when the exact sum needs another word
    /// (<see cref="GpuBigIntKernels.ReportOverflow"/>). Launched with one block.
    /// </summary>
    [GpuKernel]
    public static void CheckedScalarAddKernel([GpuReadOnly] uint[] dA, uint s, uint[] dResult, int numWords,
        uint[] status, uint origin)
    {
        var carry = DeviceScalarAddSubParallelWithCarry(dA, s, dResult, numWords, 1, false);
        if (carry != 0)
        {
            GpuBigIntKernels.ReportOverflow(status, origin);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Device_ScalarAddSubParallel(
        [GpuReadOnly] uint[] a,
        uint s,
        uint[] result,
        int numWords,
        int sign,
        bool syncThreads = true)
    {
        DeviceScalarAddSubParallelWithCarry(a, s, result, numWords, sign, syncThreads);
    }

    /// <summary>
    /// <see cref="Device_ScalarAddSubParallel"/> that also returns the carry out of the top word
    /// (<c>result[numWords - 1]</c>).
    /// </summary>
    /// <returns>
    /// The carry out of the top word (0 or 1) in the thread that wrote that word; 0 in every other thread. For a sum,
    /// 1 means the exact sum needs another word.
    /// </returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint DeviceScalarAddSubParallelWithCarry(
        [GpuReadOnly] uint[] a,
        uint s,
        uint[] result,
        int numWords,
        int sign,
        bool syncThreads)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        uint blockCarryIn = sign == 1 ? 0u : 1u;
        uint topCarry = 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? a[idx] : 0u;
            uint valB = (idx == 0) ? s : 0u;

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
                result[idx] = (uint)sum + carryIn;
            }

            if (idx == numWords - 1)
            {
                // The carry out of this word: generated here, or carried in and propagated through it.
                topCarry = GpuBigIntKernels.ApplyCarry(new GpuBigIntKernels.CarryState(g, p), carryIn);
            }

            blockCarryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);

            Gpu.SyncThreads();
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }

        return topCarry;
    }

    [GpuKernel]
    public static void ScalarMulKernel(uint[] dA, uint s, uint[] dResult, int numWords)
    {
        DeviceScalarMulParallel(dA, s, dResult, numWords, true);
    }


    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceScalarMulParallel(
        [GpuReadOnly] uint[] a,
        uint s,
        uint[] result,
        int numWords,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

        uint blockCarryIn = 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? a[idx] : 0u;
            ulong prod = (ulong)valA * s;
            uint L = (uint)(prod & 0xFFFFFFFFul);
            uint H = (uint)(prod >> 32);

            uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, H, 1);

            if (laneId == 31)
            {
                sharedH[warpId] = H;
            }
            Gpu.SyncThreads();

            if (laneId == 0)
            {
                if (warpId == 0)
                {
                    prevH = blockCarryIn;
                }
                else
                {
                    prevH = sharedH[warpId - 1];
                }
            }

            ulong sum = (ulong)L + prevH;
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

            uint carryIn = 0u;

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
                result[idx] = (uint)sum + carryIn;
            }

            uint lastH = sharedH[((bDim + 31) / 32) - 1];
            uint waveCarry = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), 0u);
            blockCarryIn = lastH + waveCarry;

            Gpu.SyncThreads();
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ScalarDivByUInt32Kernel(
        [GpuReadOnly] uint[] dA,
        uint divisor,
        uint[] dResult,
        uint[] dRemainder,
        int numWords)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            ulong currentRem = 0;
            for (int i = numWords - 1; i >= 0; i--)
            {
                currentRem = (currentRem << 32) | dA[i];
                dResult[i] = (uint)(currentRem / divisor);
                currentRem = currentRem % divisor;
            }

            dRemainder[0] = (uint)currentRem;
        }
    }
}

