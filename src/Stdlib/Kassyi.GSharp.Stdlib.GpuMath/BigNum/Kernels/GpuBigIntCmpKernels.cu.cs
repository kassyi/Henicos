namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntCmpKernels
{
    [GpuKernel]
    public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            uint notZero = dA[i] != 0 ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                notZero |= Warp.ShuffleDownSync(mask, notZero, offset);
            }
            if ((ThreadIndex.X & 31) == 0 && notZero != 0)
            {
                Gpu.AtomicOr(ref dResult[0], 1);
            }
        });
    }

    [GpuKernel]
    public static void IsOneKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            uint notOne = 0;
            if (i == 0)
            {
                notOne = dA[0] != 1 ? 1u : 0u;
            }
            else
            {
                notOne = dA[i] != 0 ? 1u : 0u;
            }

            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                notOne |= Warp.ShuffleDownSync(mask, notOne, offset);
            }

            if ((ThreadIndex.X & 31) == 0 && notOne != 0)
            {
                Gpu.AtomicOr(ref dResult[0], 1);
            }
        });
    }

    [GpuKernel]
    public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            var wa = dA[i];
            var wb = dB[i];
            ulong val = 0;
            if (wa != wb)
            {
                // Pack index into upper 32 bits, and comparison result (2 if >, 1 if <) into lower 32 bits
                long diff = wa - (long)wb;
                var signMask = (ulong)(diff >> 63);
                var resultBits = 2ul - (signMask & 1ul);
                val = ((ulong)i << 32) | resultBits;
            }

            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(mask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(mask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val)
                {
                    val = otherVal;
                }
            }

            if ((ThreadIndex.X & 31) == 0 && val != 0)
            {
                // Emits C++ atomicMax
                Gpu.AtomicMax(ref dResult[0], val);
            }
        });
    }

    [GpuKernel]
    public static void GetBitLengthKernel(uint[] dA, uint[] dResult, int numWords)
    {
        Gpu.GridStrideLoop(numWords, i =>
        {
            var w = dA[i];
            uint totalBits = 0;
            if (w != 0)
            {
                uint bitLen = 0;
                while (w > 0)
                {
                    bitLen++;
                    w >>= 1;
                }
                totalBits = (uint)(i * 32) + bitLen;
            }

            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint otherVal = Warp.ShuffleDownSync(mask, totalBits, offset);
                if (otherVal > totalBits)
                {
                    totalBits = otherVal;
                }
            }

            if ((ThreadIndex.X & 31) == 0 && totalBits != 0)
            {
                Gpu.AtomicMax(ref dResult[0], totalBits);
            }
        });
    }

    /// <summary>
    /// Writes the 64 bits below and including the top set bit of <paramref name="dA"/> to <c>dResult[1]</c> (low word)
    /// and <c>dResult[2]</c> (high word), given its bit length in <c>dResult[0]</c> (from
    /// <see cref="GetBitLengthKernel"/> on the same stream). The top set bit lands at bit 63; missing low bits are 0.
    /// </summary>
    [GpuKernel]
    public static void LeadingBitsKernel(uint[] dA, uint[] dResult, int numWords)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            var bitLength = (int)dResult[0];
            ulong leading = 0ul;
            if (bitLength > 0 && bitLength <= numWords * 32)
            {
                var wordIndex = (bitLength - 1) / 32;
                var leadingZeros = 31 - (bitLength - 1) % 32;
                ulong high = dA[wordIndex];
                ulong middle = wordIndex >= 1 ? dA[wordIndex - 1] : 0ul;
                ulong low = wordIndex >= 2 ? dA[wordIndex - 2] : 0ul;
                leading = (high << (32 + leadingZeros)) | (middle << leadingZeros) | (low >> (32 - leadingZeros));
            }

            dResult[1] = (uint)leading;
            dResult[2] = (uint)(leading >> 32);
        }
    }

    /// <summary>
    /// One side of a signed element-wise comparison: a batched vector, or a scalar broadcast to every element.
    /// </summary>
    public struct SignedCompareOperand
    {
        [GpuReadOnly]
        public uint[] Limbs;

        /// <summary>
        /// The sign word of each element (non-zero = negative), or <see langword="null"/> for a scalar.
        /// </summary>
        [GpuReadOnly]
        public uint[] Signs;

        /// <summary>The number of words this side stores per element.</summary>
        public int NumWords;

        /// <summary>The number of words between two elements; 0 for a scalar.</summary>
        public int Stride;

        /// <summary>
        /// The sign of a scalar (non-zero = negative); used when <see cref="Signs"/> is <see langword="null"/>.
        /// </summary>
        public uint ScalarSign;
    }

    public struct VectorCompareContext
    {
        public SignedCompareOperand Left;
        public SignedCompareOperand Right;
        public uint[] Result;
        public int OpType;
    }

    /// <summary>
    /// Compares element <c>BlockIndex.X</c> of the left side with the right side as signed integers and writes 1 to
    /// <c>Result</c> when the operator <c>OpType</c> (<see cref="Expressions.VectorCompareOperator"/>) holds, else 0.
    /// </summary>
    /// <remarks>
    /// Launch one block per element with a multiple of 32 threads (at most 1024), so that no warp is partial; a block
    /// narrower than the operands loops over the words, and lanes past the word count read nothing. The two sides may have different word counts; the missing upper words of the narrower side are 0. A negative
    /// zero compares equal to zero.
    /// </remarks>
    [GpuKernel]
    public static void VectorCompareKernel(VectorCompareContext ctx)
    {
        DeviceVectorCompareSignedParallel(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadCompareWord(SignedCompareOperand operand, int element, int idx)
    {
        return idx < operand.NumWords ? operand.Limbs[(element * operand.Stride) + idx] : 0u;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadCompareSign(SignedCompareOperand operand, int element)
    {
        return operand.Signs == null ? operand.ScalarSign : operand.Signs[element];
    }

    /// <summary>
    /// Combines the signs with the magnitude comparison <paramref name="magCmp"/> (0 = EQ, 1 = LT, 2 = GT) into the
    /// signed comparison, treating a negative zero as zero.
    /// </summary>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint DecideSignedCompare(uint magCmp, uint signA, bool nonzeroA, uint signB, bool nonzeroB)
    {
        bool sa = signA != 0u && nonzeroA;
        bool sb = signB != 0u && nonzeroB;
        if (sa != sb)
        {
            return sa ? 1u : 2u;
        }

        if (magCmp == 0u)
        {
            return 0u;
        }

        return sa ? (3u - magCmp) : magCmp;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareSignedParallel(VectorCompareContext ctx)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int element = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;
        int numWords = ctx.Left.NumWords > ctx.Right.NumWords ? ctx.Left.NumWords : ctx.Right.NumWords;

        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);
        SharedMemory<uint> warpNonzero = new SharedMemory<uint>(32);

        // The most significant differing word, packed as (index << 32) | (1 = LT, 2 = GT), and whether each side has a
        // non-zero word.
        ulong myVal = 0ul;
        bool myNonzeroA = false;
        bool myNonzeroB = false;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            uint wa = ReadCompareWord(ctx.Left, element, idx);
            uint wb = ReadCompareWord(ctx.Right, element, idx);
            myNonzeroA = myNonzeroA || wa != 0u;
            myNonzeroB = myNonzeroB || wb != 0u;

            if (wa != wb)
            {
                long diff = wa - (long)wb;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        uint activeMask = Warp.ActiveMask();
        uint ballotA = Warp.BallotSync(activeMask, myNonzeroA);
        uint ballotB = Warp.BallotSync(activeMask, myNonzeroB);
        if (laneId == 0)
        {
            warpNonzero[warpId] = (ballotA != 0u ? 1u : 0u) | (ballotB != 0u ? 2u : 0u);
        }

        // Synchronizes the block, so warpNonzero is complete afterwards.
        ulong maxPacked = GpuBigIntVectorKernels.ReduceBlockMaxUInt64(myVal, warpMax);

        if (tid == 0)
        {
            int numWarps = (bDim + 31) / 32;
            uint nonzero = 0u;
            for (int w = 0; w < numWarps; w++)
            {
                nonzero |= warpNonzero[w];
            }

            uint cmpRes = DecideSignedCompare(
                (uint)(maxPacked & 0xFFFFFFFFul),
                ReadCompareSign(ctx.Left, element),
                (nonzero & 1u) != 0u,
                ReadCompareSign(ctx.Right, element),
                (nonzero & 2u) != 0u);

            ctx.Result[element] = GpuBigIntVectorKernels.EvaluateCompareOperator(cmpRes, ctx.OpType) ? 1u : 0u;
        }
    }

    [GpuKernel]
    public static void VectorAndKernel(uint[] dA, uint[] dB, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            dResult[i] = (dA[i] != 0 && dB[i] != 0) ? 1u : 0u;
        });
    }

    [GpuKernel]
    public static void VectorOrKernel(uint[] dA, uint[] dB, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            dResult[i] = (dA[i] != 0 || dB[i] != 0) ? 1u : 0u;
        });
    }

    [GpuKernel]
    public static void VectorNotKernel(uint[] dA, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            dResult[i] = (dA[i] == 0) ? 1u : 0u;
        });
    }

    [GpuKernel]
    public static void VectorAnyTrueKernel(uint[] dA, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            uint isTrue = dA[i] != 0 ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                isTrue |= Warp.ShuffleDownSync(mask, isTrue, offset);
            }
            if ((ThreadIndex.X & 31) == 0 && isTrue != 0)
            {
                Gpu.AtomicMax(ref dResult[0], 1);
            }
        });
    }

    [GpuKernel]
    public static void VectorAllTrueKernel(uint[] dA, uint[] dResult, int length)
    {
        Gpu.GridStrideLoop(length, i =>
        {
            uint isFalse = dA[i] == 0 ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                isFalse |= Warp.ShuffleDownSync(mask, isFalse, offset);
            }
            if ((ThreadIndex.X & 31) == 0 && isFalse != 0)
            {
                Gpu.AtomicExch(ref dResult[0], 0); // Sets to 0 if any is false
            }
        });
    }
}
