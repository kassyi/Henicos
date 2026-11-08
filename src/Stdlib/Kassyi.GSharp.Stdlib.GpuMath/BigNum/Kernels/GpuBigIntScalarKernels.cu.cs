// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntScalarKernels
{
    /// <summary>
    /// <c>result = a + s</c>. A result wider than <paramref name="numWords"/> receives the carry in word
    /// <paramref name="numWords"/> and zeros above it; a result of <paramref name="numWords"/> words reports a carry as
    /// <see cref="GpuDomainErrors.ScalarAddOverflow"/>. Launch one block whose size is a multiple of 32.
    /// </summary>
    [GpuKernel]
    public static void ScalarAddKernel(uint[] dA, uint s, uint[] dResult, int numWords, int resultWords,
        uint[] status)
    {
        DeviceScalarAddSub(dA, s, dResult, numWords, resultWords, 0, status, GpuDomainErrors.ScalarAddOverflow);
    }

    /// <summary>
    /// <c>result = a - s</c>. A result wider than <paramref name="numWords"/> receives zeros above the difference; a
    /// borrow (<c>a &lt; s</c>) is reported as <see cref="GpuDomainErrors.ScalarSubtractBelowZero"/> at any width.
    /// Launch one block whose size is a multiple of 32.
    /// </summary>
    [GpuKernel]
    public static void ScalarSubKernel(uint[] dA, uint s, uint[] dResult, int numWords, int resultWords,
        uint[] status)
    {
        DeviceScalarAddSub(dA, s, dResult, numWords, resultWords, 1, status,
            GpuDomainErrors.ScalarSubtractBelowZero);
    }

    // a + s (subtract = 0) or a - s (subtract = 1) over numWords words, then the word above them. The array a may be
    // the result itself (x++): each thread reads its word before it writes it.
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceScalarAddSub(
        uint[] a,
        uint s,
        uint[] result,
        int numWords,
        int resultWords,
        int subtract,
        uint[] status,
        uint code)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);

        // a - s is a + ~s + 1
        uint blockCarryIn = subtract == 1 ? 1u : 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? a[idx] : 0u;
            uint valB = idx == 0 ? s : 0u;
            if (subtract == 1)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = GpuBigIntVectorKernels.PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId,
                bDim, out blockCarryIn);

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            // The carry out of the top word, taken from its own thread: the block carry of a partial last warp is not
            // defined (its last lane does not exist)
            if (idx == numWords - 1)
            {
                topCarry[0] = g | (p & carryIn);
            }

            Gpu.SyncThreads();
        }

        // Adding: the carry out. Subtracting: 1 - carry out, the borrow.
        uint carryOut = subtract == 1 ? 1u - topCarry[0] : topCarry[0];

        for (int i = numWords + tid; i < resultWords; i += bDim)
        {
            result[i] = i == numWords && subtract == 0 ? carryOut : 0u;
        }

        bool fails = subtract == 1 ? carryOut != 0u : carryOut != 0u && resultWords == numWords;
        if (fails && tid == 0)
        {
            GpuDomainErrors.Report(status, code);
        }
    }

    /// <summary>
    /// <c>result = a * s</c>. A result wider than <paramref name="numWords"/> receives the top word of the product in
    /// word <paramref name="numWords"/> and zeros above it; a result of <paramref name="numWords"/> words reports a
    /// non-zero top word as <see cref="GpuDomainErrors.ScalarMultiplyOverflow"/>. Launch one block whose size is a
    /// multiple of 32.
    /// </summary>
    [GpuKernel]
    public static void ScalarMulKernel(uint[] dA, uint s, uint[] dResult, int numWords, int resultWords,
        uint[] status)
    {
        DeviceScalarMul(dA, s, dResult, numWords, resultWords, status);
    }

    // a * s over numWords words, then the top word. The array a may be the result itself: within a pass every thread
    // reads before the block synchronizes and writes after it, and the high half of the product of the word before the
    // pass is carried in shared memory instead of being read again.
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceScalarMul(
        uint[] a,
        uint s,
        uint[] result,
        int numWords,
        int resultWords,
        uint[] status)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> lastHigh = new SharedMemory<uint>(1);
        SharedMemory<uint> topWord = new SharedMemory<uint>(1);

        if (tid == 0)
        {
            lastHigh[0] = 0u;
        }

        Gpu.SyncThreads();

        uint blockCarryIn = 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            ulong prod = (ulong)(valid ? a[idx] : 0u) * s;
            uint low = (uint)prod;
            uint high = (uint)(prod >> 32);

            // The high half of the word below: from the same pass, or from the last thread of the previous pass
            uint prevHigh = lastHigh[0];
            if (tid > 0)
            {
                prevHigh = idx - 1 < numWords ? (uint)(((ulong)a[idx - 1] * s) >> 32) : 0u;
            }

            ulong sum = (ulong)low + prevHigh;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = GpuBigIntVectorKernels.PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId,
                bDim, out blockCarryIn);

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            // high <= 2^32 - 2 because a word and s are at most 2^32 - 1, so adding the carry does not wrap
            if (idx == numWords - 1)
            {
                topWord[0] = high + (g | (p & carryIn));
            }

            if (tid == bDim - 1)
            {
                lastHigh[0] = high;
            }

            Gpu.SyncThreads();
        }

        uint top = topWord[0];
        for (int i = numWords + tid; i < resultWords; i += bDim)
        {
            result[i] = i == numWords ? top : 0u;
        }

        if (top != 0u && resultWords == numWords && tid == 0)
        {
            GpuDomainErrors.Report(status, GpuDomainErrors.ScalarMultiplyOverflow);
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
