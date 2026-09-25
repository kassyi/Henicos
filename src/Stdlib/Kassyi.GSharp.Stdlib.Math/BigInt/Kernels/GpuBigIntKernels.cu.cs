namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

using Contexts;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntKernels
{
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
        int sign,
        bool syncThreads = true)
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

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ParallelAddKernel([GpuReadOnly] uint[] dA, [GpuReadOnly] uint[] dB, uint[] dResult, int numWords)
    {
        DeviceAddSubParallel(dA, dB, dResult, numWords, 1, false);
    }

    [GpuKernel]
    public static void ParallelSubKernel([GpuReadOnly] uint[] dA, [GpuReadOnly] uint[] dB, uint[] dResult, int numWords)
    {
        DeviceAddSubParallel(dA, dB, dResult, numWords, -1, false);
    }

    [GpuKernel]
    public static void ShiftLeftKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

            uint w0 = dA[safeIdx0];
            uint w1 = dA[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            dResult[i] = (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
        });
    }

    [GpuKernel]
    public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

            uint w0 = dA[safeIdx0];
            uint w1 = dA[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            dResult[i] = (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);
        });
    }

    [GpuKernel]
    public static void BitwiseAndKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = dA[i] & dB[i];
        });
    }

    [GpuKernel]
    public static void BitwiseOrKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = dA[i] | dB[i];
        });
    }

    [GpuKernel]
    public static void BitwiseXorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = dA[i] ^ dB[i];
        });
    }

    [GpuKernel]
    public static void BitwiseNotKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = ~dA[i];
        });
    }

    #region MacroTranspiler & Device Helpers

    [GpuDeviceFunction]
    public static void Device_ShiftLeft(BigIntKernelContext ctx, int wordShift, int bitShift)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx1);

            uint w0 = ctx.Operands.LeftOperand[safeIdx0];
            uint w1 = ctx.Operands.LeftOperand[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            ctx.Operands.ResultBuffer[i] = (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
        });
    }

    [GpuDeviceFunction]
    public static void Device_ShiftRight(BigIntKernelContext ctx, int wordShift, int bitShift)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx1);

            uint w0 = ctx.Operands.LeftOperand[safeIdx0];
            uint w1 = ctx.Operands.LeftOperand[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            ctx.Operands.ResultBuffer[i] = (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseAnd(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = context.Operands.LeftOperand[i] & context.Operands.RightOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseOr(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = context.Operands.LeftOperand[i] | context.Operands.RightOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseXor(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = context.Operands.LeftOperand[i] ^ context.Operands.RightOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseNot(ref BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = ~context.Operands.LeftOperand[i];
        });
    }

    [GpuDeviceFunction]
    public static void Device_Add(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        ulong carry = 0;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = Gpu.AddWithCarry(left[i], right[i], ref carry);
        }
    }

    [GpuDeviceFunction]
    public static void Device_Sub(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        long borrow = 0;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = Gpu.SubWithBorrow(left[i], right[i], ref borrow);
        }
    }

    [GpuDeviceFunction]
    public static void Device_ShiftLeft(ref BigIntKernelContext ctx, uint[] result, uint[] left, int shiftBits)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx1);

            uint w0 = left[safeIdx0];
            uint w1 = left[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            result[i] = (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
        }
    }

    [GpuDeviceFunction]
    public static void Device_ShiftRight(ref BigIntKernelContext ctx, uint[] result, uint[] left, int shiftBits)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= ctx.NumberOfWords ? ctx.NumberOfWords - 1 : srcIdx1);

            uint w0 = left[safeIdx0];
            uint w1 = left[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < ctx.NumberOfWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            result[i] = (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseAnd(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] & right[i];
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseOr(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] | right[i];
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseXor(ref BigIntKernelContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] ^ right[i];
        }
    }

    [GpuDeviceFunction]
    public static void Device_BitwiseNot(ref BigIntKernelContext ctx, uint[] result, uint[] operand)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = ~operand[i];
        }
    }

    #endregion
}
