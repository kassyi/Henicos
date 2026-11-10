// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Kernels of the signed expression nodes whose result sign depends on the values: they read the operands' signs and
/// write the sign of the result into one device word, so that no sign is read on the host between the nodes and a
/// CUDA graph can capture them.
/// </summary>
/// <remarks>
/// An operand's sign is given by a word and a host value: <c>hostSign</c> 0 or 1 is the sign itself, -1 means "read
/// <c>sign[0]</c>" (a sign another kernel wrote). A sign word is 1 for negative. A zero result has the sign 0, as
/// <c>BigInteger</c> has no negative zero. Launch one block whose size is a multiple of 32.
/// </remarks>
[SkipSnapshotConsistency("Tested in separate suites")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntSignedKernels
{
    /// <summary>The sign of an operand: <paramref name="hostSign"/>, or <c>sign[0]</c> when it is -1.</summary>
    [GpuDeviceFunction, GpuInline]
    public static uint ReadSign([GpuReadOnly] uint[] sign, int hostSign) => hostSign < 0 ? sign[0] : (uint)hostSign;

    /// <summary>
    /// <c>result = a + b</c> (<paramref name="subtract"/> = 0) or <c>a - b</c> (1) of signed values over
    /// <paramref name="resultWords"/> words, at least one more than the wider magnitude: magnitudes of the same
    /// effective sign are added, different ones subtracted, and a borrow negates the difference and takes the second
    /// operand's effective sign.
    /// </summary>
    [GpuKernel]
    public static void SignedAddSubKernel([GpuReadOnly] uint[] a, int aWords, [GpuReadOnly] uint[] aSign,
        int aHostSign, [GpuReadOnly] uint[] b, int bWords, [GpuReadOnly] uint[] bSign, int bHostSign, int subtract,
        uint[] result, int resultWords, uint[] resultSign)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);
        SharedMemory<uint> nonZero = new SharedMemory<uint>(1);
        if (tid == 0)
        {
            nonZero[0] = 0u;
        }

        Gpu.SyncThreads();

        uint signA = ReadSign(aSign, aHostSign);
        uint signB = ReadSign(bSign, bHostSign) ^ (uint)subtract;
        bool differ = signA != signB;

        // a - b is a + ~b + 1 over the result's words
        uint blockCarryIn = differ ? 1u : 0u;
        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            int idx = offset + tid;
            uint valA = idx < aWords ? a[idx] : 0u;
            uint valB = idx < bWords ? b[idx] : 0u;
            if (differ)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = GpuBigIntVectorKernels.PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId,
                bDim, out blockCarryIn);

            uint word = (uint)sum + carryIn;
            if (idx < resultWords)
            {
                result[idx] = word;
                if (word != 0u)
                {
                    nonZero[0] = 1u;
                }
            }

            // The carry out of the top word, from its own thread (the block carry of a partial warp is not defined)
            if (idx == resultWords - 1)
            {
                topCarry[0] = g | (p & carryIn);
            }

            Gpu.SyncThreads();
        }

        // No carry out of a + ~b + 1 is a borrow: |b| > |a|, and the result holds the difference modulo the width
        bool borrowed = differ && topCarry[0] == 0u;
        if (borrowed)
        {
            NegateInPlace(result, resultWords, sharedCarry, tid, bDim, laneId, warpId);
        }

        if (tid == 0)
        {
            uint sign = borrowed ? signB : signA;
            resultSign[0] = nonZero[0] == 0u ? 0u : sign;
        }
    }

    /// <summary>
    /// <c>result = Max(a, b)</c> (<paramref name="pickMax"/> = 1) or <c>Min(a, b)</c> (0) of signed values: the chosen
    /// operand's magnitude in the first words of the result, zeros above it, and its sign flag as it is (the result
    /// is one of the operands, so <c>-0</c> stays <c>-0</c>). Equal values choose <paramref name="a"/>; <c>-0</c>
    /// equals <c>+0</c>.
    /// </summary>
    [GpuKernel]
    public static void SignedSelectKernel([GpuReadOnly] uint[] a, int aWords, [GpuReadOnly] uint[] aSign,
        int aHostSign, [GpuReadOnly] uint[] b, int bWords, [GpuReadOnly] uint[] bSign, int bHostSign, int pickMax,
        uint[] result, int resultWords, uint[] resultSign)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;

        SharedMemory<uint> nonZero = new SharedMemory<uint>(2);
        SharedMemory<uint> chooseA = new SharedMemory<uint>(1);
        if (tid == 0)
        {
            nonZero[0] = 0u;
            nonZero[1] = 0u;
        }

        Gpu.SyncThreads();

        for (int i = tid; i < aWords; i += bDim)
        {
            if (a[i] != 0u)
            {
                nonZero[0] = 1u;
            }
        }

        for (int i = tid; i < bWords; i += bDim)
        {
            if (b[i] != 0u)
            {
                nonZero[1] = 1u;
            }
        }

        Gpu.SyncThreads();

        if (tid == 0)
        {
            uint flagA = ReadSign(aSign, aHostSign);
            uint flagB = ReadSign(bSign, bHostSign);
            bool negativeA = flagA != 0u && nonZero[0] != 0u;
            bool negativeB = flagB != 0u && nonZero[1] != 0u;

            // The signed comparison of a and b: -1, 0 or 1
            int compare;
            if (negativeA != negativeB)
            {
                compare = negativeA ? -1 : 1;
            }
            else
            {
                int magnitude = 0;
                int words = aWords > bWords ? aWords : bWords;
                for (int i = words - 1; i >= 0 && magnitude == 0; i--)
                {
                    uint wordA = i < aWords ? a[i] : 0u;
                    uint wordB = i < bWords ? b[i] : 0u;
                    if (wordA != wordB)
                    {
                        magnitude = wordA > wordB ? 1 : -1;
                    }
                }

                compare = negativeA ? -magnitude : magnitude;
            }

            bool takeA = pickMax == 1 ? compare >= 0 : compare <= 0;
            chooseA[0] = takeA ? 1u : 0u;
            resultSign[0] = takeA ? flagA : flagB;
        }

        Gpu.SyncThreads();

        bool fromA = chooseA[0] != 0u;
        int chosenWords = fromA ? aWords : bWords;
        for (int i = tid; i < resultWords; i += bDim)
        {
            uint word = 0u;
            if (i < chosenWords)
            {
                word = fromA ? a[i] : b[i];
            }

            result[i] = word;
        }
    }

    /// <summary>
    /// <c>result = ~x</c> (<c>-x - 1</c>) of a signed value over <paramref name="resultWords"/> words, one more than
    /// the magnitude: <c>-(|x| + 1)</c> for a non-negative <c>x</c>; <c>|x| - 1</c> with + for a negative one, and
    /// <c>-1</c> for <c>-0</c>.
    /// </summary>
    [GpuKernel]
    public static void SignedNotKernel([GpuReadOnly] uint[] x, int xWords, [GpuReadOnly] uint[] xSign, int xHostSign,
        uint[] result, int resultWords, uint[] resultSign)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);

        bool negative = ReadSign(xSign, xHostSign) != 0u;

        // |x| + 1, or |x| - 1 as |x| + (all ones) without the carry in
        uint blockCarryIn = negative ? 0u : 1u;
        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            int idx = offset + tid;
            uint valA = idx < xWords ? x[idx] : 0u;
            uint valB = negative ? 0xFFFFFFFFu : 0u;

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = GpuBigIntVectorKernels.PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId,
                bDim, out blockCarryIn);

            if (idx < resultWords)
            {
                result[idx] = (uint)sum + carryIn;
            }

            if (idx == resultWords - 1)
            {
                topCarry[0] = g | (p & carryIn);
            }

            Gpu.SyncThreads();
        }

        // |x| - 1 borrowed only for a zero magnitude (-0): ~(-0) is -1
        bool negativeZero = negative && topCarry[0] == 0u;
        if (negativeZero)
        {
            for (int i = tid; i < resultWords; i += bDim)
            {
                result[i] = i == 0 ? 1u : 0u;
            }
        }

        if (tid == 0)
        {
            resultSign[0] = negative && !negativeZero ? 0u : 1u;
        }
    }

    /// <summary>
    /// The sign of a remainder: the dividend's sign when <paramref name="remainder"/> is not zero, 0 otherwise.
    /// </summary>
    [GpuKernel]
    public static void RemainderSignKernel([GpuReadOnly] uint[] remainder, int words, [GpuReadOnly] uint[] dividendSign,
        int dividendHostSign, uint[] resultSign)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        SharedMemory<uint> nonZero = new SharedMemory<uint>(1);
        if (tid == 0)
        {
            nonZero[0] = 0u;
        }

        Gpu.SyncThreads();

        for (int i = tid; i < words; i += bDim)
        {
            if (remainder[i] != 0u)
            {
                nonZero[0] = 1u;
            }
        }

        Gpu.SyncThreads();

        if (tid == 0)
        {
            resultSign[0] = nonZero[0] == 0u ? 0u : ReadSign(dividendSign, dividendHostSign);
        }
    }

    /// <summary>
    /// <c>resultSign = signA ^ signB</c>: the sign of a product or quotient, of <c>-x</c> (with a host 1), or a copy
    /// (with a host 0). Launch one thread.
    /// </summary>
    [GpuKernel]
    public static void SignXorKernel([GpuReadOnly] uint[] aSign, int aHostSign, [GpuReadOnly] uint[] bSign,
        int bHostSign, uint[] resultSign)
    {
        if (ThreadIndex.X == 0)
        {
            resultSign[0] = ReadSign(aSign, aHostSign) ^ ReadSign(bSign, bHostSign);
        }
    }

    // x = ~x + 1 over words words, in place, each thread reading and writing its own words
    [GpuDeviceFunction]
    public static void NegateInPlace(uint[] x, int words, SharedMemory<uint> sharedCarry, int tid, int bDim,
        int laneId, int warpId)
    {
        uint blockCarryIn = 1u;
        for (int offset = 0; offset < words; offset += bDim)
        {
            int idx = offset + tid;
            uint value = idx < words ? ~x[idx] : 0u;
            uint p = value == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = GpuBigIntVectorKernels.PropagateBlockCarry(0u, p, blockCarryIn, sharedCarry, laneId, warpId,
                bDim, out blockCarryIn);
            if (idx < words)
            {
                x[idx] = value + carryIn;
            }

            Gpu.SyncThreads();
        }
    }
}
