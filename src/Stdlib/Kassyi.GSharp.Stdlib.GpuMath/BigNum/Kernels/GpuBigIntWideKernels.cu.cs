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
    /// <c>result = a + b</c> (<paramref name="subtract"/> = 0) or <c>a - b</c> (1) over <paramref name="resultWords"/>
    /// words. The sum or difference is computed over every word of the operands, so a result that does not fit in
    /// <paramref name="resultWords"/> words (<see cref="GpuDomainErrors.AddOverflow"/>,
    /// <see cref="GpuDomainErrors.SubtractOverflow"/>) and a difference below zero
    /// (<see cref="GpuDomainErrors.SubtractBelowZero"/>) are reported, never dropped. Launch one block whose size is a
    /// multiple of 32. The result may be <paramref name="a"/> or <paramref name="b"/> itself, but must not only partly
    /// overlap them.
    /// </summary>
    [GpuKernel]
    public static void AddSubWideKernel(uint[] a, int aWords, uint[] b, int bWords, uint[] result, int resultWords,
        int subtract, uint[] status)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);
        SharedMemory<uint> highWords = new SharedMemory<uint>(1);

        if (tid == 0)
        {
            highWords[0] = 0u;
        }

        Gpu.SyncThreads();

        int words = aWords > bWords ? aWords : bWords;
        if (resultWords > words)
        {
            words = resultWords;
        }

        // a - b is a + ~b + 1
        uint blockCarryIn = subtract == 1 ? 1u : 0u;

        for (int offset = 0; offset < words; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < words;

            uint valA = idx < aWords ? a[idx] : 0u;
            uint valB = idx < bWords ? b[idx] : 0u;
            if (subtract == 1)
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
            else if (valid && word != 0u)
            {
                // A word above the result: the value does not fit
                highWords[0] = 1u;
            }

            // The carry out of the top word, taken from its own thread: the block carry of a partial last warp is not
            // defined (its last lane does not exist)
            if (idx == words - 1)
            {
                topCarry[0] = g | (p & carryIn);
            }

            Gpu.SyncThreads();
        }

        if (tid == 0)
        {
            if (subtract == 1 && topCarry[0] == 0u)
            {
                GpuDomainErrors.Report(status, GpuDomainErrors.SubtractBelowZero);
            }
            else if (subtract == 1 && highWords[0] != 0u)
            {
                GpuDomainErrors.Report(status, GpuDomainErrors.SubtractOverflow);
            }
            else if (subtract == 0 && (topCarry[0] != 0u || highWords[0] != 0u))
            {
                GpuDomainErrors.Report(status, GpuDomainErrors.AddOverflow);
            }
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
    /// Reports <see cref="GpuDomainErrors.FitOverflow"/> once when a word of <paramref name="a"/> at or above
    /// <paramref name="words"/> is not zero. Launch one block.
    /// </summary>
    [GpuKernel]
    public static void FitCheckKernel([GpuReadOnly] uint[] a, int aWords, int words, uint[] status)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        SharedMemory<uint> highWords = new SharedMemory<uint>(1);

        if (tid == 0)
        {
            highWords[0] = 0u;
        }

        Gpu.SyncThreads();

        for (int i = words + tid; i < aWords; i += bDim)
        {
            if (a[i] != 0u)
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
}
