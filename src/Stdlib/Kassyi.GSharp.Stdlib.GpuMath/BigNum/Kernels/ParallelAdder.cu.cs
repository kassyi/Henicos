namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class ParallelAdder
{
    /// <summary>
    /// Computes the warp-wide inclusive prefix sum of carry generation (<c>G</c>) and propagation (<c>P</c>) bits
    /// using the work-efficient two-phase Brent-Kung tree algorithm (Up-Sweep reduction followed by Down-Sweep distribution).
    /// </summary>
    /// <param name="state">The initial carry state containing <c>Generate</c> and <c>Propagate</c> bits for the calling lane.</param>
    /// <param name="laneId">The thread's lane index within the 32-thread warp (<c>0</c> to <c>31</c>).</param>
    /// <returns>The warp-wide inclusive prefix sum state for the calling lane.</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuBigIntKernels.CarryState WarpScanGpBrentKung(GpuBigIntKernels.CarryState state, int laneId)
    {
        uint g = state.Generate;
        uint p = state.Propagate;

        // Phase 1: Up-Sweep (Reduction Tree) for strides d = 1, 2, 4, 8, 16
        for (int d = 1; d < 32; d <<= 1)
        {
            uint prevG = Warp.ShuffleUpSync(0xFFFFFFFFu, g, (uint)d);
            uint prevP = Warp.ShuffleUpSync(0xFFFFFFFFu, p, (uint)d);

            if (((laneId + 1) & ((d << 1) - 1)) == 0)
            {
                g = g | (p & prevG);
                p = p & prevP;
            }
        }

        // Phase 2: Down-Sweep (Distribution Tree) for strides d = 8, 4, 2, 1
        for (int d = 8; d >= 1; d >>= 1)
        {
            uint prevG = Warp.ShuffleUpSync(0xFFFFFFFFu, g, (uint)d);
            uint prevP = Warp.ShuffleUpSync(0xFFFFFFFFu, p, (uint)d);

            if (laneId >= d && ((laneId + 1 - d) & ((d << 1) - 1)) == 0)
            {
                g = g | (p & prevG);
                p = p & prevP;
            }
        }

        return new GpuBigIntKernels.CarryState(g, p);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word addition (<c>result = a + b</c>) purely in registers using Kogge-Stone warp shuffle prefix scans,
    /// without requiring shared memory or thread-block barrier synchronization.
    /// </summary>
    /// <param name="a">The first multi-word unsigned integer operand.</param>
    /// <param name="b">The second multi-word unsigned integer operand.</param>
    /// <param name="result">The destination buffer receiving the sum words.</param>
    /// <returns>The final carry-out bit (<c>0</c> or <c>1</c>) from the most significant word.</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AddParallelWarpSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result)
    {
        return AddSubParallelWarpSync(a, b, result, a.Length);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word addition (<c>result = a + b</c>) for an explicit word count using Kogge-Stone warp shuffle prefix scans.
    /// </summary>
    /// <param name="a">The first multi-word unsigned integer operand.</param>
    /// <param name="b">The second multi-word unsigned integer operand.</param>
    /// <param name="result">The destination buffer receiving the sum words.</param>
    /// <param name="numWords">The number of 32-bit words to add.</param>
    /// <returns>The final carry-out bit (<c>0</c> or <c>1</c>) from the most significant word.</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AddParallelWarpSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result,
        int numWords)
    {
        return AddSubParallelWarpSync(a, b, result, numWords);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word subtraction (<c>result = a - b</c>) purely in registers using Kogge-Stone warp shuffle prefix scans,
    /// without requiring shared memory or thread-block barrier synchronization.
    /// </summary>
    /// <param name="a">The minuend multi-word unsigned integer operand.</param>
    /// <param name="b">The subtrahend multi-word unsigned integer operand.</param>
    /// <param name="result">The destination buffer receiving the difference words.</param>
    /// <returns>The final two's-complement carry-out bit (<c>1</c> if no borrow occurred, <c>0</c> if borrow occurred).</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SubParallelWarpSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result)
    {
        return AddSubParallelWarpSync(a, b, result, a.Length, -1);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word subtraction (<c>result = a - b</c>) for an explicit word count using Kogge-Stone warp shuffle prefix scans.
    /// </summary>
    /// <param name="a">The minuend multi-word unsigned integer operand.</param>
    /// <param name="b">The subtrahend multi-word unsigned integer operand.</param>
    /// <param name="result">The destination buffer receiving the difference words.</param>
    /// <param name="numWords">The number of 32-bit words to subtract.</param>
    /// <returns>The final two's-complement carry-out bit (<c>1</c> if no borrow occurred, <c>0</c> if borrow occurred).</returns>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SubParallelWarpSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result,
        int numWords)
    {
        return AddSubParallelWarpSync(a, b, result, numWords, -1);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word addition or subtraction in 32-word chunks using Kogge-Stone warp shuffle prefix scans.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AddSubParallelWarpSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result,
        int numWords,
        int sign = 1)
    {
        int laneId = ThreadIndex.X & 31;
        uint warpCarryIn = sign == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWords; offset += 32)
        {
            int idx = offset + laneId;
            bool valid = idx < numWords;

            uint valA = valid ? a[idx] : 0u;
            uint valB = valid ? b[idx] : 0u;

            if (sign == -1)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            uint scanG = warpScan.Generate;
            uint scanP = warpScan.Propagate;

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1u);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1u);

            uint carryIn = warpCarryIn;
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            uint laneCarryOut = GpuBigIntKernels.ApplyCarry(warpScan, warpCarryIn);
            warpCarryIn = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut, 31);
        }

        return warpCarryIn;
    }

    /// <summary>
    /// Performs warp-cooperative multi-word addition (<c>result = a + b</c>) purely in registers using the Brent-Kung warp shuffle prefix scan tree.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AddParallelWarpBrentKungSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result)
    {
        return AddSubParallelWarpBrentKungSync(a, b, result, a.Length);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word subtraction (<c>result = a - b</c>) purely in registers using the Brent-Kung warp shuffle prefix scan tree.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SubParallelWarpBrentKungSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result)
    {
        return AddSubParallelWarpBrentKungSync(a, b, result, a.Length, -1);
    }

    /// <summary>
    /// Performs warp-cooperative multi-word addition or subtraction in 32-word chunks using the Brent-Kung prefix scan tree.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AddSubParallelWarpBrentKungSync(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result,
        int numWords,
        int sign = 1)
    {
        int laneId = ThreadIndex.X & 31;
        uint warpCarryIn = sign == 1 ? 0u : 1u;

        for (int offset = 0; offset < numWords; offset += 32)
        {
            int idx = offset + laneId;
            bool valid = idx < numWords;

            uint valA = valid ? a[idx] : 0u;
            uint valB = valid ? b[idx] : 0u;

            if (sign == -1)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = WarpScanGpBrentKung(new GpuBigIntKernels.CarryState(g, p), laneId);
            uint scanG = warpScan.Generate;
            uint scanP = warpScan.Propagate;

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1u);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1u);

            uint carryIn = warpCarryIn;
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            uint laneCarryOut = GpuBigIntKernels.ApplyCarry(warpScan, warpCarryIn);
            warpCarryIn = Warp.ShuffleSync(0xFFFFFFFFu, laneCarryOut, 31);
        }

        return warpCarryIn;
    }

    /// <summary>
    /// Performs block-cooperative multi-word addition or subtraction across warps using the Brent-Kung parallel prefix tree.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddSubParallelBrentKung(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int sign = 1,
        bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        var sharedCarry = new SharedMemory<uint>(32);

        if (BlockIndex.X != 0)
        {
            if (syncThreads)
            {
                Gpu.SyncThreads();
            }

            return;
        }

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
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = WarpScanGpBrentKung(new GpuBigIntKernels.CarryState(g, p), laneId);
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
                    : new GpuBigIntKernels.CarryState(0u, 1u);
                var blockScan = WarpScanGpBrentKung(valState, laneId);

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

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1u);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1u);
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            blockCarryIn = GpuBigIntKernels.ApplyCarry(
                GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]),
                blockCarryIn);

            Gpu.SyncThreads();
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void WarpParallelAddKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords)
    {
        if (BlockIndex.X != 0 || ThreadIndex.X >= 32)
        {
            return;
        }

        if (numWords == a.Length)
        {
            AddParallelWarpSync(a, b, result);
        }
        else
        {
            AddParallelWarpSync(a, b, result, numWords);
        }
    }

    [GpuKernel]
    public static void WarpParallelSubKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords)
    {
        if (BlockIndex.X != 0 || ThreadIndex.X >= 32)
        {
            return;
        }

        if (numWords == a.Length)
        {
            SubParallelWarpSync(a, b, result);
        }
        else
        {
            SubParallelWarpSync(a, b, result, numWords);
        }
    }

    [GpuKernel]
    public static void WarpBrentKungParallelAddKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords)
    {
        if (BlockIndex.X != 0 || ThreadIndex.X >= 32)
        {
            return;
        }

        if (numWords == a.Length)
        {
            AddParallelWarpBrentKungSync(a, b, result);
        }
        else
        {
            AddSubParallelWarpBrentKungSync(a, b, result, numWords);
        }
    }

    [GpuKernel]
    public static void WarpBrentKungParallelSubKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords)
    {
        if (BlockIndex.X != 0 || ThreadIndex.X >= 32)
        {
            return;
        }

        if (numWords == a.Length)
        {
            SubParallelWarpBrentKungSync(a, b, result);
        }
        else
        {
            AddSubParallelWarpBrentKungSync(a, b, result, numWords, -1);
        }
    }

    [GpuKernel]
    public static void BrentKungParallelAddKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords)
    {
        AddSubParallelBrentKung(a, b, result, numWords, sign: 1, syncThreads: false);
    }

    [GpuKernel]
    public static void BrentKungParallelSubKernel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords)
    {
        AddSubParallelBrentKung(a, b, result, numWords, sign: -1, syncThreads: false);
    }
}
