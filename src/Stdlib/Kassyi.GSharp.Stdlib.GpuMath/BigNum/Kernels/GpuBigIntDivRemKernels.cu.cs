namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntDivRemKernels
{
    /// <summary>The correction status written when the ReduceZeroSync correction does not converge.</summary>
    public const uint ReduceZeroSyncCorrectionFailed = 1u;

    /// <summary>The correction status written when the reciprocal correction does not converge.</summary>
    public const uint ReciprocalCorrectionFailed = 2u;

    /// <summary>The correction status written when the inverse square root correction does not converge.</summary>
    public const uint InverseSqrtCorrectionFailed = 3u;

    /// <summary>The correction status written when the integer square root correction does not converge.</summary>
    public const uint SqrtCorrectionFailed = 4u;

    // Dummy kernel to force the generator to emit the DualNtt struct definition before we use it
    [GpuDeviceFunction]
    private static void ForceEmitDualNttForDivRem(DualNtt args)
    {
    }

    /// <summary>
    /// Multiplies <paramref name="opA"/> and <paramref name="opB"/> by dual-prime NTT inside the Newton-Raphson
    /// mega-kernels. Overwrites <c>core.Workspace.NttScratch.Product</c> and <c>core.Workspace.NttScratch.Kept1</c>.
    /// </summary>
    [GpuDeviceFunction]
    public static void ExecuteNttMultiplyMacro(
        NewtonRaphsonContext core,
        uint[] opA, uint[] opB, uint[] resultDest,
        int numWordsA, int numWordsB, int numWordsResult,
        int chunkSize)
    {
        var nttScratch = core.Workspace.NttScratch;
        ExecuteNttTransformOperand(core, opA, numWordsA, nttScratch.Product);
        ExecuteNttTransformOperand(core, opB, numWordsB, nttScratch.Kept1);
        ExecuteNttPointwiseResidues(core, nttScratch.Product, nttScratch.Kept1, nttScratch.Product);
        ExecuteNttResiduesToWords(core, nttScratch.Product, resultDest, numWordsResult, chunkSize);
    }

    [GpuKernel(Cooperative = true)]
    public static void ReciprocalNewtonRaphsonMegaKernel(NewtonRaphsonContext core)
    {
        var dims = core.Dimensions;
        var ops = core.Operands;
        var iterScratch = core.Workspace.Iteration;
        var nttScratch = core.Workspace.NttScratch;
        uint[] currentProduct = Gpu.NotNull(iterScratch.CurrentProduct);
        uint[] constant = Gpu.NotNull(ops.Constant);
        int n = dims.NumberOfWords;
        int resultWords = (dims.TargetBits + 31) / 32;

        // The carry chunk of every product is one block of threads (see ExecuteResolveNttCarriesBlocked).
        int chunkSize = BlockDimension.X;

        // Kept2 holds b̂ = T(targetOperand), invariant across all iterations and the final correction.
        // Kept1 holds x̂ = T(resultBuffer), updated once per iteration and reused for b*x and x*diff.
        ExecuteNttTransformOperand(core, ops.TargetOperand, n, nttScratch.Kept2);

        for (int i = 0; i < dims.Iterations; i++)
        {
            // 1. x̂ = T(resultBuffer), currentProduct = b * x (n, resultWords -> n + resultWords)
            ExecuteNttTransformOperand(core, ops.ResultBuffer, resultWords, nttScratch.Kept1);
            ExecuteNttMultiplyKept(core, nttScratch.Kept1, nttScratch.Kept2, currentProduct, n + resultWords,
                chunkSize);

            // 2. diff = constant - currentProduct (block-parallel subtraction on block 0)
            int subWords = n + resultWords;
            GpuBigIntKernels.DeviceAddSubAlignedParallel(constant, subWords, currentProduct, subWords,
                iterScratch.Diff, subWords, 0, 0, -1, true);
            Gpu.SyncGrid();

            // 3. nextIter = x * diff (resultWords, n + resultWords -> n + 2 * resultWords)
            uint[] nextIter = Gpu.NotNull(iterScratch.NextIter);
            ExecuteNttMultiplyByKept(core, iterScratch.Diff, n + resultWords, nttScratch.Kept1, nextIter,
                n + 2 * resultWords, chunkSize);

            // 4. copy resultBuffer = nextIter >> targetBits
            ShiftRightIntoResult(nextIter, n + 2 * resultWords, ops.ResultBuffer, resultWords, dims.TargetBits);
        }

        // Final correction to x = floor(2^T / b): r = 2^T - b * x, then r moves into [0, b) and x by the same steps.
        // The Newton result is floor(2^T / b) or one less, so this takes at most one step after convergence.
        ExecuteNttTransformOperand(core, ops.ResultBuffer, resultWords, nttScratch.Kept1);
        ExecuteNttMultiplyKept(core, nttScratch.Kept1, nttScratch.Kept2, currentProduct, n + resultWords, chunkSize);

        // Constant holds 2^(T+1) in n + 2 * resultWords words; reading it shifted right by one bit gives 2^T.
        int remainderWords = n + resultWords;
        GpuBigIntKernels.DeviceAddSubAlignedParallel(currentProduct, remainderWords, constant,
            n + 2 * resultWords, iterScratch.Diff, remainderWords, 0, 1, -2, true);
        var delta = CorrectIntoDivisorRange(iterScratch.Diff, ops.TargetOperand, remainderWords, n, 1000,
            Gpu.NotNull(core.CorrectionStatus), ReciprocalCorrectionFailed);
        Gpu.SingleThread(() => AddSmallSigned(ops.ResultBuffer, resultWords, delta));
    }

    /// <summary>
    /// Corrects the remainder <c>diff = a - q * b</c> into <c>[0, b)</c> and the quotient <paramref name="quotient"/> by
    /// the same number of steps, so that both are exact when the estimate is within 1000 steps of <c>q</c>.
    /// </summary>
    /// <remarks>
    /// Launched with one block; runs on the stream without a host synchronization. When the estimate is further away,
    /// <see cref="ReduceZeroSyncCorrectionFailed"/> is written to <paramref name="status"/>.
    /// </remarks>
    [GpuKernel]
    public static void BarrettCorrectionKernel(uint[] diff, [GpuReadOnly] uint[] rightOperand, uint[] quotient,
        int evalWords, int quotientWords, uint[] status)
    {
        var delta = CorrectIntoDivisorRange(diff, rightOperand, evalWords, evalWords, 1000, status,
            ReduceZeroSyncCorrectionFailed);
        Gpu.SingleThread(() => AddSmallSigned(quotient, quotientWords, delta));
    }

    /// <summary>
    /// Subtracts 1 from <paramref name="estimate"/> when <paramref name="product"/> is greater than
    /// <paramref name="bound"/>: the correction of a quotient or an inverse square root computed from a truncated
    /// operand, which is the exact floor or one above it (#661 §4.12).
    /// </summary>
    /// <param name="product">The estimate times the full operand (<c>Q·R</c>, or <c>x²·A</c>).</param>
    /// <param name="productWords">The word count of <paramref name="product"/>.</param>
    /// <param name="bound">The value the product must not exceed (<c>N</c>, or <c>2^(2B)</c>).</param>
    /// <param name="boundWords">The word count of <paramref name="bound"/>; words above the shorter of the two buffers
    /// count as 0.</param>
    /// <param name="estimate">The estimate, at least 1, decremented in place.</param>
    /// <param name="estimateWords">The word count of <paramref name="estimate"/>.</param>
    /// <remarks>
    /// Launched with one block; runs on the stream without a host synchronization. The threads find the most
    /// significant differing word together; thread 0 then decrements the estimate.
    /// </remarks>
    [GpuKernel]
    public static void TruncatedBoundCorrectionKernel([GpuReadOnly] uint[] product, int productWords,
        [GpuReadOnly] uint[] bound, int boundWords, uint[] estimate, int estimateWords)
    {
        var warpMax = new SharedMemory<ulong>(32);

        // Launched with one block; any other block would decrement the estimate again.
        if (BlockIndex.X != 0)
        {
            return;
        }

        // The most significant differing word, packed as (index << 32) | (1 = less, 2 = greater); 0 when equal.
        var words = productWords > boundWords ? productWords : boundWords;
        ulong packed = 0ul;
        for (int idx = ThreadIndex.X; idx < words; idx += BlockDimension.X)
        {
            uint left = idx < productWords ? product[idx] : 0u;
            uint right = idx < boundWords ? bound[idx] : 0u;
            if (left != right)
            {
                packed = ((ulong)idx << 32) | (left > right ? 2ul : 1ul);
            }
        }

        ulong maxPacked = GpuBigIntVectorKernels.ReduceBlockMaxUInt64(packed, warpMax);
        if (ThreadIndex.X == 0 && (maxPacked & 0xFFFFFFFFul) == 2ul)
        {
            AddSmallSigned(estimate, estimateWords, -1);
        }
    }

    /// <summary>
    /// Moves a signed remainder (two's complement over <paramref name="remainderWords"/> words) into
    /// <c>[0, divisor)</c> by adding or subtracting the divisor one step at a time.
    /// </summary>
    /// <returns>The number of steps to add to the quotient: +1 per subtraction, -1 per addition of the divisor.</returns>
    /// <remarks>
    /// Runs on block 0 only (the other blocks return 0 at once). Every thread of block 0 reads the same sign word after a
    /// barrier, so all of them take the same branches and return the same count. The remainder needs one word more than
    /// its magnitude so that the top bit is the sign. When the remainder is not confirmed inside the range within
    /// <paramref name="maxSteps"/> steps, thread 0 adds 1 to <c>status[0]</c> and writes <paramref name="failureCode"/> to
    /// <c>status[1]</c>; the host reports it at the next read of the stream.
    /// </remarks>
    [GpuDeviceFunction]
    public static int CorrectIntoDivisorRange(uint[] remainder, [GpuReadOnly] uint[] divisor, int remainderWords,
        int divisorWords, int maxSteps, uint[] status, uint failureCode)
    {
        if (BlockIndex.X != 0)
        {
            return 0;
        }

        int delta = 0;
        bool converged = false;

        // At most maxSteps moves; the pass after the last move only checks the range, so a remainder exactly maxSteps
        // steps away is still confirmed.
        for (int step = 0; step <= maxSteps; step++)
        {
            var negative = (remainder[remainderWords - 1] & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (negative)
            {
                if (step == maxSteps)
                {
                    break;
                }

                // r < 0: the quotient estimate was too large.
                GpuBigIntKernels.DeviceAddSubAlignedParallel(remainder, remainderWords, divisor, divisorWords,
                    remainder, remainderWords, 0, 0, 1, true);
                delta--;
                continue;
            }

            GpuBigIntKernels.DeviceAddSubAlignedParallel(remainder, remainderWords, divisor, divisorWords, remainder,
                remainderWords, 0, 0, -1, true);
            var belowZero = (remainder[remainderWords - 1] & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (belowZero || step == maxSteps)
            {
                // r was already below the divisor (or no move is left): undo the subtraction and stop.
                GpuBigIntKernels.DeviceAddSubAlignedParallel(remainder, remainderWords, divisor, divisorWords,
                    remainder, remainderWords, 0, 0, 1, true);
                converged = belowZero;
                break;
            }

            delta++;
        }

        if (!converged && ThreadIndex.X == 0)
        {
            // status[0] counts the failures (it only grows) and status[1] keeps the code of the last one.
            Gpu.AtomicAdd(ref status[0], 1u);
            status[1] = failureCode;
        }

        return delta;
    }

    /// <summary>
    /// Adds a small signed count to a multi-word unsigned value in place, propagating the carry or the borrow.
    /// </summary>
    /// <remarks>Serial; call it from one thread (<see cref="Gpu.SingleThread"/>).</remarks>
    [GpuDeviceFunction]
    public static void AddSmallSigned(uint[] value, int numWords, int delta)
    {
        if (delta > 0)
        {
            ulong carry = (ulong)delta;
            for (int i = 0; i < numWords && carry != 0; i++)
            {
                ulong sum = value[i] + carry;
                value[i] = (uint)sum;
                carry = sum >> 32;
            }
        }
        else if (delta < 0)
        {
            long borrow = -(long)delta;
            for (int i = 0; i < numWords && borrow != 0; i++)
            {
                long difference = (long)value[i] - borrow;
                if (difference < 0)
                {
                    value[i] = (uint)(difference + 4294967296L);
                    borrow = 1;
                }
                else
                {
                    value[i] = (uint)difference;
                    borrow = 0;
                }
            }
        }
    }
}
