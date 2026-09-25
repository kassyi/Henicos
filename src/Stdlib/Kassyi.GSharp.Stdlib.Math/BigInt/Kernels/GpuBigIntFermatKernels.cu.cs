namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntFermatKernels
{
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords)
    {
        Gpu.GridStrideLoop(n, i =>
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
        });
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceTranspose32X32([GpuReadOnly] uint[] idata, uint[] odata, int width, int height)
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

        Gpu.ForUnroll(0, 4, iter =>
        {
            var i = iter * 8;
            if (xIndex < width && yIndex + i < height)
            {
                Gpu.InlinePtx($"s_mem[({ty} + {i}) * 33 + {tx}] = {idata[(yIndex + i) * width + xIndex]};");
            }
        });

        Gpu.SyncThreads();

        var outXIndex = by * 32 + tx;
        var outYIndex = bx * 32 + ty;

        Gpu.ForUnroll(0, 4, iter =>
        {
            var i = iter * 8;
            if (outXIndex >= height || outYIndex + i >= width)
            {
                return;
            }

            uint val = 0;
            Gpu.InlinePtx($"{val} = s_mem[{tx} * 33 + ({ty} + {i})];");
            odata[(outYIndex + i) * height + outXIndex] = val;
        });
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceShiftLeftParallel([GpuReadOnly] uint[] src, uint[] dst, int shiftBits, int numWords)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        Gpu.GridStrideLoop(numWords, i =>
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

            uint w0 = src[safeIdx0];
            uint w1 = src[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            dst[i] = (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
        });
    }


    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint WarpScanGp(uint val, int laneId)
    {
        uint g = val & 1u;
        uint p = (val >> 1) & 1u;

        for (int i = 1; i < 32; i *= 2)
        {
            uint prevG = Warp.ShuffleUpSync(0xFFFFFFFFu, g, (uint)i);
            uint prevP = Warp.ShuffleUpSync(0xFFFFFFFFu, p, (uint)i);

            if (laneId < i)
            {
                continue;
            }

            g = g | (p & prevG);
            p = p & prevP;
        }

        return (p << 1) | g;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ApplyCarry(uint gp, uint carryIn)
    {
        uint g = gp & 1u;
        uint p = (gp >> 1) & 1u;
        return g | (p & carryIn);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceAddSubParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int sign)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32); // Max 1024 threads / 32 = 32 warps

        uint blockCarryIn = sign == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? a[idx] : 0u;
            uint valB = valid ? b[idx] : 0u;

            if (sign == -1)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint warpScan = WarpScanGp((p << 1) | g, laneId);
            uint scanG = warpScan & 1u;
            uint scanP = (warpScan >> 1) & 1u;

            if (laneId == 31)
            {
                sharedCarry[warpId] = warpScan;
            }

            Gpu.SyncThreads();

            if (warpId == 0)
            {
                int numWarps = bDim / 32;
                uint val = laneId < numWarps ? sharedCarry[laneId] : 0u;
                uint blockScan = WarpScanGp(val, laneId);

                if (laneId < numWarps)
                {
                    sharedCarry[laneId] = blockScan;
                }
            }

            Gpu.SyncThreads();

            uint carryIn = blockCarryIn;

            if (warpId > 0)
            {
                carryIn = ApplyCarry(sharedCarry[warpId - 1], carryIn);
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

            blockCarryIn = ApplyCarry(sharedCarry[(bDim / 32) - 1], blockCarryIn);

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void KernelTestBlockAddSubParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int sign)
    {
        DeviceAddSubParallel(a, b, result, numWords, sign);
    }

    [GpuKernel]
    public static void KernelFermatReduceMega(
        [GpuReadOnly] uint[] valueT,
        uint[] mulResult,
        uint[] quotient,
        uint[] remainder,
        uint[] temp,
        [GpuReadOnly] uint[] remainderMask,
        int numWords,
        int outNumWords,
        int totalShiftBits,
        int exponent)
    {
        var wordShift = totalShiftBits / 32;
        var bitShift = totalShiftBits % 32;

        Gpu.ForUnroll(0, 3, iter =>
        {
            Gpu.GridStrideLoop(outNumWords, i =>
            {
                var srcIdx0 = i + wordShift;
                var srcIdx1 = srcIdx0 + 1;

                var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords * 2 ? numWords * 2 - 1 : srcIdx0);
                var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords * 2 ? numWords * 2 - 1 : srcIdx1);

                uint w0 = mulResult[safeIdx0];
                uint w1 = mulResult[safeIdx1];

                uint mask0 = srcIdx0 >= 0 && srcIdx0 < numWords * 2 ? 0xFFFFFFFFu : 0u;
                uint mask1 = srcIdx1 >= 0 && srcIdx1 < numWords * 2 ? 0xFFFFFFFFu : 0u;

                w0 &= mask0;
                w1 &= mask1;

                uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
                quotient[i] = (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);

                remainder[i] = mulResult[i] & remainderMask[i];
            });

            Gpu.SyncThreads();

            DeviceShiftLeftParallel(quotient, temp, exponent, outNumWords);
            Gpu.SyncThreads();

            DeviceAddSubParallel(remainder, temp, remainder, outNumWords, 1);
            Gpu.SyncThreads();

            // Subtract Q * 8
            DeviceShiftLeftParallel(quotient, temp, 3, outNumWords);
            Gpu.SyncThreads();
            DeviceAddSubParallel(remainder, temp, remainder, outNumWords, -1);
            Gpu.SyncThreads();

            // Subtract Q * 2
            DeviceShiftLeftParallel(quotient, temp, 1, outNumWords);
            Gpu.SyncThreads();
            DeviceAddSubParallel(remainder, temp, remainder, outNumWords, -1);
            Gpu.SyncThreads();

            // Subtract Q * 1
            DeviceAddSubParallel(remainder, quotient, remainder, outNumWords, -1);
            Gpu.SyncThreads();

            if (iter >= 2)
            {
                return;
            }

            Gpu.GridStrideLoop(outNumWords, i =>
            {
                mulResult[i] = remainder[i];
            });
            Gpu.SyncThreads();
        });
    }
}


