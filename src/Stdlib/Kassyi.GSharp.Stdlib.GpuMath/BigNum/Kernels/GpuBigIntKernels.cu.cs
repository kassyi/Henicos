namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntKernels
{
    /// <summary>
    /// Represents the carry generation and propagation bits for prefix sum operations.
    /// </summary>
    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct CarryState(uint generate, uint propagate)
    {
        public uint Generate = generate;
        public uint Propagate = propagate;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PackCarryState(CarryState state) => (state.Propagate << 1) | state.Generate;

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CarryState UnpackCarryState(uint packed) => new(packed & 1u, (packed >> 1) & 1u);

    /// <summary>
    /// Computes the warp-wide prefix sum of carry generation (G) and propagation (P) bits using the Kogge-Stone algorithm.
    /// </summary>
    /// <param name="state">The carry state containing Generate and Propagate bits for the current thread.</param>
    /// <param name="laneId">The thread's lane index within the warp (0 to 31).</param>
    /// <returns>The warp-wide inclusive prefix sum result containing the accumulated Generate and Propagate bits.</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CarryState WarpScanGp(CarryState state, int laneId)
    {
        var g = state.Generate;
        var p = state.Propagate;

        for (int i = 1; i < 32; i *= 2)
        {
            var prevG = Warp.ShuffleUpSync(0xFFFFFFFFu, g, (uint)i);
            var prevP = Warp.ShuffleUpSync(0xFFFFFFFFu, p, (uint)i);

            if (laneId < i)
            {
                continue;
            }

            g = g | (p & prevG);
            p = p & prevP;
        }

        return new CarryState(g, p);
    }

    /// <summary>
    /// Applies an incoming carry to the carry state.
    /// </summary>
    /// <param name="state">The carry state containing Generate and Propagate bits, typically produced by <see cref="WarpScanGp"/>.</param>
    /// <param name="carryIn">The carry-in bit to propagate through the P bit.</param>
    /// <returns>The final resolved carry bit (0 or 1) for the current segment.</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ApplyCarry(CarryState state, uint carryIn)
    {
        return state.Generate | (state.Propagate & carryIn);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetShiftedRightWord(
        [GpuReadOnly] uint[] data,
        int numWords,
        int destIdx,
        int wordShift,
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

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceAddSubAlignedParallel(
        [GpuReadOnly] uint[] larger,
        int largerWords,
        [GpuReadOnly] uint[] smaller,
        int smallerWords,
        uint[] result,
        int numWords,
        int wordShift,
        int bitShift,
        int sign = 1,
        bool syncThreads = true)
    {
        var tid = ThreadIndex.X;
        var bDim = BlockDimension.X;
        var laneId = tid % 32;
        var warpId = tid / 32;

        var sharedCarry = new SharedMemory<uint>(32); // Max 1024 threads / 32 = 32 warps

        if (BlockIndex.X != 0)
        {
            if (syncThreads)
            {
                Gpu.SyncThreads();
            }
            return;
        }

        var blockCarryIn = sign == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            var idx = offset + tid;
            var valid = idx < numWords;

            var valA = (valid && idx < largerWords) ? larger[idx] : 0u;
            var valB = valid
                ? GetShiftedRightWord(smaller, smallerWords, idx, wordShift, bitShift)
                : 0u;

            if (sign == -1)
            {
                valB = ~valB;
            }
            else if (sign == -2)
            {
                valA = ~valA;
            }

            var sum = (ulong)valA + valB;
            var g = (uint)(sum >> 32);
            var p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            var warpScan = WarpScanGp(new CarryState(g, p), laneId);
            var scanG = warpScan.Generate;
            var scanP = warpScan.Propagate;

            if (laneId == 31)
            {
                sharedCarry[warpId] = PackCarryState(warpScan);
            }

            Gpu.SyncThreads();

            if (warpId == 0)
            {
                var numWarps = (bDim + 31) / 32;
                var valState = laneId < numWarps ? UnpackCarryState(sharedCarry[laneId]) : new CarryState(0, 0);
                var blockScan = WarpScanGp(valState, laneId);

                if (laneId < numWarps)
                {
                    sharedCarry[laneId] = PackCarryState(blockScan);
                }
            }

            Gpu.SyncThreads();

            var carryIn = blockCarryIn;

            if (warpId > 0)
            {
                carryIn = ApplyCarry(UnpackCarryState(sharedCarry[warpId - 1]), carryIn);
            }

            var prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
            var prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            blockCarryIn = ApplyCarry(UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), blockCarryIn);

            Gpu.SyncThreads();
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceAddSubParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int sign = 1,
        bool syncThreads = true)
    {
        DeviceAddSubAlignedParallel(a, numWords, b, numWords, result, numWords, 0, 0, sign, syncThreads);
    }

    [GpuKernel]
    public static void ParallelAddKernel([GpuReadOnly] uint[] dA, [GpuReadOnly] uint[] dB, uint[] dResult, int numWords)
    {
        DeviceAddSubParallel(dA, dB, dResult, numWords, syncThreads: false);
    }

    [GpuKernel]
    public static void ParallelSubKernel([GpuReadOnly] uint[] dA, [GpuReadOnly] uint[] dB, uint[] dResult, int numWords)
    {
        DeviceAddSubParallel(dA, dB, dResult, numWords, sign: -1, syncThreads: false);
    }

    [GpuKernel]
    public static void ShiftLeftKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = ShiftedLeftWord(dA, i, wordShift, bitShift, numWords);
        });
    }

    [GpuKernel]
    public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            dResult[i] = ShiftedRightWord(dA, i, wordShift, bitShift, numWords);
        });
    }

    /// <summary>
    /// <c>a = a &lt;&lt; (32 wordShift + bitShift)</c> in place. Launch one block whose size is a multiple of 32. The
    /// passes run from the top words down: word <c>i</c> reads words <c>i - wordShift</c> and the one below, never a
    /// word above it, and each pass reads all its words before the block writes them, so no thread reads a word that
    /// was already shifted.
    /// </summary>
    [GpuKernel]
    public static void ShiftLeftInPlaceKernel(uint[] dA, int wordShift, int bitShift, int numWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        for (int top = numWords; top > 0; top -= bDim)
        {
            int i = top - bDim + tid;
            uint word = i >= 0 ? ShiftedLeftWord(dA, i, wordShift, bitShift, numWords) : 0u;
            Gpu.SyncThreads();
            if (i >= 0)
            {
                dA[i] = word;
            }
        }
    }

    /// <summary>
    /// <c>a = a &gt;&gt; (32 wordShift + bitShift)</c> in place. Launch one block whose size is a multiple of 32. The
    /// passes run from the bottom words up: word <c>i</c> reads words <c>i + wordShift</c> and the one above, never a
    /// word below it, and each pass reads all its words before the block writes them.
    /// </summary>
    [GpuKernel]
    public static void ShiftRightInPlaceKernel(uint[] dA, int wordShift, int bitShift, int numWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int i = offset + tid;
            uint word = i < numWords ? ShiftedRightWord(dA, i, wordShift, bitShift, numWords) : 0u;
            Gpu.SyncThreads();
            if (i < numWords)
            {
                dA[i] = word;
            }
        }
    }

    // Word i of a << (32 wordShift + bitShift): the bits of words i - wordShift and the one below; a word outside the
    // array reads as zero
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ShiftedLeftWord([GpuReadOnly] uint[] dA, int i, int wordShift, int bitShift, int numWords)
    {
        var srcIdx0 = i - wordShift;
        var srcIdx1 = srcIdx0 - 1;

        var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
        var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

        var w0 = dA[safeIdx0];
        var w1 = dA[safeIdx1];

        var mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
        var mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

        w0 &= mask0;
        w1 &= mask1;

        var shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
        return (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
    }

    // Word i of a >> (32 wordShift + bitShift): the bits of words i + wordShift and the one above; a word outside the
    // array reads as zero
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ShiftedRightWord([GpuReadOnly] uint[] dA, int i, int wordShift, int bitShift, int numWords)
    {
        var srcIdx0 = i + wordShift;
        var srcIdx1 = srcIdx0 + 1;

        var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
        var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

        var w0 = dA[safeIdx0];
        var w1 = dA[safeIdx1];

        var mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
        var mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

        w0 &= mask0;
        w1 &= mask1;

        var shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
        return (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);
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
    public static void ExecuteShiftLeft(GpuBigIntContext ctx, int wordShift, int bitShift)
    {
        var left = Gpu.NotNull(ctx.Operands.LeftOperand);
        var result = ctx.Operands.ResultBuffer;
        if (IsSameArray(left, result))
        {
            // The result is the operand: the threads of the grid-stride loop would read words that other threads
            // already shifted, so one thread of the grid shifts every word, downward
            Gpu.SingleThread(() => ShiftWordsLeft(result, left, wordShift, bitShift, ctx.NumberOfWords));
            return;
        }

        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            result[i] = ShiftedLeftWord(left, i, wordShift, bitShift, ctx.NumberOfWords);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteShiftRight(GpuBigIntContext ctx, int wordShift, int bitShift)
    {
        var left = Gpu.NotNull(ctx.Operands.LeftOperand);
        var result = ctx.Operands.ResultBuffer;
        if (IsSameArray(left, result))
        {
            // As for the left shift: one thread of the grid shifts every word, upward
            Gpu.SingleThread(() => ShiftWordsRight(result, left, wordShift, bitShift, ctx.NumberOfWords));
            return;
        }

        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            result[i] = ShiftedRightWord(left, i, wordShift, bitShift, ctx.NumberOfWords);
        });
    }

    // Whether a and b are the same array (two null or two empty arrays count as the same, which shifts nothing)
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool IsSameArray([GpuReadOnly] uint[] a, [GpuReadOnly] uint[] b)
    {
        fixed (uint* pa = a, pb = b)
        {
            return pa == pb;
        }
    }

    // result = left << (32 wordShift + bitShift) on numWords words, by one thread. Downward: word i reads words
    // i - wordShift and the one below, so when result is left every word read is one not yet written
    [GpuDeviceFunction]
    private static void ShiftWordsLeft(uint[] result, [GpuReadOnly] uint[] left, int wordShift, int bitShift,
        int numWords)
    {
        for (int i = numWords - 1; i >= 0; i--)
        {
            result[i] = ShiftedLeftWord(left, i, wordShift, bitShift, numWords);
        }
    }

    // result = left >> (32 wordShift + bitShift) on numWords words, by one thread. Upward: word i reads words
    // i + wordShift and the one above, so when result is left every word read is one not yet written
    [GpuDeviceFunction]
    private static void ShiftWordsRight(uint[] result, [GpuReadOnly] uint[] left, int wordShift, int bitShift,
        int numWords)
    {
        for (int i = 0; i < numWords; i++)
        {
            result[i] = ShiftedRightWord(left, i, wordShift, bitShift, numWords);
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseAnd(ref GpuBigIntContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = Gpu.NotNull(context.Operands.LeftOperand)[i] &
                                               Gpu.NotNull(context.Operands.RightOperand)[i];
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseOr(ref GpuBigIntContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = Gpu.NotNull(context.Operands.LeftOperand)[i] |
                                               Gpu.NotNull(context.Operands.RightOperand)[i];
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseXor(ref GpuBigIntContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = Gpu.NotNull(context.Operands.LeftOperand)[i] ^
                                               Gpu.NotNull(context.Operands.RightOperand)[i];
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseNot(ref GpuBigIntContext ctx)
    {
        Gpu.GridStrideLoop(ref ctx, ctx.NumberOfWords, static (ref context, i) =>
        {
            context.Operands.ResultBuffer[i] = ~Gpu.NotNull(context.Operands.LeftOperand)[i];
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteAdd(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right)
    {
        ulong carry = 0;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = Gpu.AddWithCarry(left[i], right[i], ref carry);
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteSub(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right)
    {
        long borrow = 0;
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = Gpu.SubWithBorrow(left[i], right[i], ref borrow);
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteShiftLeft(ref GpuBigIntContext ctx, uint[] result, uint[] left, int shiftBits)
    {
        // x << -n is x >> n, as for BigInteger; the amount is widened so that -int.MinValue is 2^31
        long amount = shiftBits;
        if (amount < 0)
        {
            ShiftWordsRight(result, left, (int)(-amount / 32), (int)(-amount % 32), ctx.NumberOfWords);
            return;
        }

        ShiftWordsLeft(result, left, (int)(amount / 32), (int)(amount % 32), ctx.NumberOfWords);
    }

    [GpuDeviceFunction]
    public static void ExecuteShiftRight(ref GpuBigIntContext ctx, uint[] result, uint[] left, int shiftBits)
    {
        // x >> -n is x << n, as for BigInteger
        long amount = shiftBits;
        if (amount < 0)
        {
            ShiftWordsLeft(result, left, (int)(-amount / 32), (int)(-amount % 32), ctx.NumberOfWords);
            return;
        }

        ShiftWordsRight(result, left, (int)(amount / 32), (int)(amount % 32), ctx.NumberOfWords);
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseAnd(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] & right[i];
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseOr(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] | right[i];
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseXor(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = left[i] ^ right[i];
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteBitwiseNot(ref GpuBigIntContext ctx, uint[] result, uint[] operand)
    {
        for (int i = 0; i < ctx.NumberOfWords; i++)
        {
            result[i] = ~operand[i];
        }
    }

    #endregion
}
