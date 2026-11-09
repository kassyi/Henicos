// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Kernels whose operands and result each have their own word count. A missing word of an operand reads as zero.
/// </summary>
[SkipSnapshotConsistency("Tested in separate suites")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntWideKernels
{
    /// <summary>The operation of <see cref="BitwiseWideKernel"/>: <c>a &amp; b</c>.</summary>
    public const int BitwiseAnd = 0;

    /// <summary>The operation of <see cref="BitwiseWideKernel"/>: <c>a | b</c>.</summary>
    public const int BitwiseOr = 1;

    /// <summary>The operation of <see cref="BitwiseWideKernel"/>: <c>a ^ b</c>.</summary>
    public const int BitwiseXor = 2;

    /// <summary>The operation of <see cref="BitwiseWideKernel"/>: <c>~a</c> over the words of <c>a</c>.</summary>
    public const int BitwiseNot = 3;

    /// <summary>
    /// Added to the operation of <see cref="AddSubWideKernel"/>: the result is taken modulo its width and not
    /// reported (an algorithm that works in two's complement on purpose).
    /// </summary>
    public const int ModuloMode = 2;

    /// <summary>
    /// <c>result = a + b</c> (<paramref name="subtract"/> = 0) or <c>a - b</c> (1) over <paramref name="resultWords"/>
    /// words. The sum or difference is computed over every word of the operands, so a result that does not fit in
    /// <paramref name="resultWords"/> words (<see cref="GpuDomainErrors.AddOverflow"/>,
    /// <see cref="GpuDomainErrors.SubtractOverflow"/>) and a difference below zero
    /// (<see cref="GpuDomainErrors.SubtractBelowZero"/>) are reported, never dropped. With
    /// <see cref="ModuloMode"/> added to <paramref name="subtract"/> the result is the sum or difference modulo
    /// <c>2^(32 resultWords)</c>, and nothing is reported. Launch one block whose size is a
    /// multiple of 32. The result may be <paramref name="a"/> or <paramref name="b"/> itself, but must not only partly
    /// overlap them.
    /// </summary>
    [GpuKernel]
    public static void AddSubWideKernel(uint[] a, int aWords, uint[] b, int bWords, uint[] result, int resultWords,
        int subtract, uint[] status)
    {
        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);
        SharedMemory<uint> highWords = new SharedMemory<uint>(1);
        ClearFlag(highWords);

        bool sub = (subtract & 1) == 1;
        bool modular = (subtract & ModuloMode) != 0;

        // Over every word of the operands and of the result, so that a word that does not fit is seen
        int words = aWords > bWords ? aWords : bWords;
        if (resultWords > words)
        {
            words = resultWords;
        }

        AddWords(a, aWords, b, bWords, sub, result, resultWords, words, sharedCarry, topCarry, highWords);

        if (ThreadIndex.X == 0 && !modular)
        {
            ReportAddSubtract(status, sub, topCarry[0], highWords[0]);
        }
    }

    /// <summary>
    /// Reports the domain error of <see cref="AddSubWideKernel"/>, if any: a borrow out of the top word (a difference
    /// below zero), a non-zero word above the result (a value that does not fit), or a carry out of a sum.
    /// </summary>
    [GpuDeviceFunction]
    public static void ReportAddSubtract(uint[] status, bool subtract, uint topCarry, uint highWords)
    {
        if (subtract && topCarry == 0u)
        {
            GpuDomainErrors.Report(status, GpuDomainErrors.SubtractBelowZero);
        }
        else if (subtract && highWords != 0u)
        {
            GpuDomainErrors.Report(status, GpuDomainErrors.SubtractOverflow);
        }
        else if (!subtract && (topCarry != 0u || highWords != 0u))
        {
            GpuDomainErrors.Report(status, GpuDomainErrors.AddOverflow);
        }
    }

    /// <summary>
    /// <c>result = a op b</c> (<see cref="BitwiseAnd"/>, <see cref="BitwiseOr"/>, <see cref="BitwiseXor"/>) or
    /// <c>~a</c> (<see cref="BitwiseNot"/>, whose words above <paramref name="aWords"/> are zero) over
    /// <paramref name="resultWords"/> words.
    /// </summary>
    [GpuKernel]
    public static void BitwiseWideKernel(uint[] a, int aWords, uint[] b, int bWords, uint[] result, int resultWords,
        int operation)
    {
        Gpu.GridStrideLoop(resultWords, i =>
        {
            uint valA = i < aWords ? a[i] : 0u;
            uint valB = i < bWords ? b[i] : 0u;
            uint word = operation == BitwiseAnd ? valA & valB
                : operation == BitwiseOr ? valA | valB
                : operation == BitwiseXor ? valA ^ valB
                : i < aWords ? ~valA : 0u;

            result[i] = word;
        });
    }

    /// <summary>
    /// Reports <see cref="GpuDomainErrors.FitOverflow"/> once when a bit of <paramref name="a"/> at or above a bit
    /// position is set: a bit of word <paramref name="firstWord"/> in <paramref name="firstMask"/>, or any bit of a
    /// word above it. Launch one block.
    /// </summary>
    [GpuKernel]
    public static void FitCheckKernel([GpuReadOnly] uint[] a, int aWords, int firstWord, uint firstMask,
        uint[] status)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        SharedMemory<uint> highWords = new SharedMemory<uint>(1);

        if (tid == 0)
        {
            highWords[0] = 0u;
        }

        Gpu.SyncThreads();

        for (int i = firstWord + tid; i < aWords; i += bDim)
        {
            uint mask = i == firstWord ? firstMask : 0xFFFFFFFFu;
            if ((a[i] & mask) != 0u)
            {
                highWords[0] = 1u;
            }
        }

        Gpu.SyncThreads();

        if (tid == 0 && highWords[0] != 0u)
        {
            GpuDomainErrors.Report(status, GpuDomainErrors.FitOverflow);
        }
    }

    /// <summary>
    /// <c>result = |a - b|</c> over <paramref name="resultWords"/> words, at least as many as each operand: the
    /// difference, and its two's complement when it borrowed. Writes <c>flags[0] = 1</c> when it borrowed
    /// (<c>b &gt; a</c>) and <c>flags[1] = 1</c> when the result is not zero. Launch one block whose size is a multiple
    /// of 32. The result must not overlap an operand.
    /// </summary>
    [GpuKernel]
    public static void MagnitudeDifferenceKernel([GpuReadOnly] uint[] a, int aWords, [GpuReadOnly] uint[] b,
        int bWords, uint[] result, int resultWords, uint[] flags)
    {
        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);
        SharedMemory<uint> highWords = new SharedMemory<uint>(1);
        SharedMemory<uint> nonZero = new SharedMemory<uint>(1);
        ClearFlag(highWords);
        ClearFlag(nonZero);

        // a - b over the result's words, which hold both operands
        AddWords(a, aWords, b, bWords, true, result, resultWords, resultWords, sharedCarry, topCarry, highWords);

        // No carry out of a + ~b + 1 is a borrow: b > a, and the result holds a - b modulo the width
        bool borrowed = topCarry[0] == 0u;
        if (borrowed)
        {
            NegateWords(result, resultWords, sharedCarry);
        }

        MarkNonZero(result, resultWords, nonZero);

        if (ThreadIndex.X == 0)
        {
            flags[0] = borrowed ? 1u : 0u;
            flags[1] = nonZero[0];
        }
    }

    /// <summary>
    /// One carry chain over <paramref name="words"/> words: <c>a + b</c>, or <c>a + ~b + 1</c> (<c>a - b</c>) when
    /// <paramref name="complementB"/> is set, a missing word of an operand reading as zero. Writes the words below
    /// <paramref name="resultWords"/>, sets <c>highWords[0]</c> when a word at or above them is not zero, and leaves
    /// the carry out of the top word in <c>topCarry[0]</c>, taken from its own thread (the block carry of a partial
    /// last warp is not defined). Every thread of the block calls it.
    /// </summary>
    [GpuDeviceFunction]
    public static void AddWords([GpuReadOnly] uint[] a, int aWords, [GpuReadOnly] uint[] b, int bWords,
        bool complementB, uint[] result, int resultWords, int words, SharedMemory<uint> sharedCarry,
        SharedMemory<uint> topCarry, SharedMemory<uint> highWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        uint blockCarryIn = complementB ? 1u : 0u;
        for (int offset = 0; offset < words; offset += bDim)
        {
            int idx = offset + tid;
            uint valA = idx < aWords ? a[idx] : 0u;
            uint valB = idx < bWords ? b[idx] : 0u;
            if (complementB)
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
            }
            else if (idx < words && word != 0u)
            {
                highWords[0] = 1u;
            }

            if (idx == words - 1)
            {
                topCarry[0] = g | (p & carryIn);
            }

            Gpu.SyncThreads();
        }
    }

    /// <summary>
    /// <c>x = ~x + 1</c> (two's complement) over <paramref name="words"/> words, in place, each thread reading and
    /// writing its own words. Every thread of the block calls it.
    /// </summary>
    [GpuDeviceFunction]
    public static void NegateWords(uint[] x, int words, SharedMemory<uint> sharedCarry)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

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

    /// <summary>Sets <c>flag[0]</c> when a word of <paramref name="x"/> is not zero; synchronizes the block.</summary>
    [GpuDeviceFunction]
    public static void MarkNonZero([GpuReadOnly] uint[] x, int words, SharedMemory<uint> flag)
    {
        for (int i = ThreadIndex.X; i < words; i += BlockDimension.X)
        {
            if (x[i] != 0u)
            {
                flag[0] = 1u;
            }
        }

        Gpu.SyncThreads();
    }

    /// <summary>Clears <c>flag[0]</c> from thread 0 and synchronizes the block.</summary>
    [GpuDeviceFunction]
    public static void ClearFlag(SharedMemory<uint> flag)
    {
        if (ThreadIndex.X == 0)
        {
            flag[0] = 0u;
        }

        Gpu.SyncThreads();
    }
}
