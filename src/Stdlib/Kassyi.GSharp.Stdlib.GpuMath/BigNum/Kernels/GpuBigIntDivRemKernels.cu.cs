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
    private static void ForceEmitDualNtt_DivRem(DualNtt args)
    {
    }

    [GpuDeviceFunction]
    public static void ExecuteNttMultiplyMacro(
        NewtonRaphsonContext core,
        uint[] opA, uint[] opB, uint[] resultDest,
        int numWordsA, int numWordsB, int numWordsResult,
        int chunkSize)
    {
        var m = core.Ntt.TransformLength;
        int n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        int n1 = m / n2;
        if (m <= 512)
        {
            n1 = m;
            n2 = 1;
        }

        var nttScratch = core.Workspace.NttScratch;

        // 1. Unpack
        GpuBigIntFermatKernels.DeviceUnpack16(opA, nttScratch.NttBuf1A, nttScratch.NttBuf2A, m, numWordsA);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, nttScratch.NttBuf1B, nttScratch.NttBuf2B, m, numWordsB);
        Gpu.SyncGrid();

        // 2. Base NTT Context
        var baseNtt = new DualNtt
        {
            TemporaryBuffer = nttScratch.TmpBuf,
            Grid = new NttGridContext { M = m, N1 = n1, N2 = n2 }
        };

        // 3. Forward NTT A (Prime 1 & 2)
        var forwardA = baseNtt;
        forwardA.Buffer1 = nttScratch.NttBuf1A;
        forwardA.Buffer2 = nttScratch.NttBuf2A;
        forwardA.Twiddle1 = core.Ntt.TwiddleFactors.Forward1;
        forwardA.Twiddle2 = core.Ntt.TwiddleFactors.Forward2;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardA);

        // 4. Forward NTT B (Prime 1 & 2)
        var forwardB = forwardA; // Twiddles are the same, just update buffers
        forwardB.Buffer1 = nttScratch.NttBuf1B;
        forwardB.Buffer2 = nttScratch.NttBuf2B;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardB);

        // 5. Pointwise Multiply
        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(nttScratch.NttBuf1A, nttScratch.NttBuf2A,
            nttScratch.NttBuf1B, nttScratch.NttBuf2B, m);
        Gpu.SyncGrid();

        // 6. Inverse NTT (Prime 1 & 2)
        var inverseA = baseNtt;
        inverseA.Buffer1 = nttScratch.NttBuf1A;
        inverseA.Buffer2 = nttScratch.NttBuf2A;
        inverseA.Twiddle1 = core.Ntt.TwiddleFactors.Inverse1;
        inverseA.Twiddle2 = core.Ntt.TwiddleFactors.Inverse2;
        GpuBigIntNttKernels.ExecuteDualInverseNtt(inverseA);

        // 6. Compute CRT
        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(nttScratch.NttBuf1A, nttScratch.NttBuf2A,
            nttScratch.UncarriedLow, nttScratch.UncarriedHigh, m);
        Gpu.SyncGrid();

        // 7-8. Resolve Carries (Blocked & Apply)
        int numBlocks = (numWordsResult + chunkSize - 1) / chunkSize;
        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(nttScratch.UncarriedLow, nttScratch.UncarriedHigh,
            resultDest, nttScratch.BlockCarries, m, numWordsResult, chunkSize);
        Gpu.SyncGrid();
        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, nttScratch.BlockCarries, numWordsResult,
            chunkSize, numBlocks);
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    public static void ReciprocalNewtonRaphsonMegaKernel(ReciprocalNewtonRaphson ctx)
    {
        var core = ctx.Core;
        var dims = core.Dimensions;
        var ops = core.Operands;
        var iterScratch = core.Workspace.Iteration;
        int n = dims.NumberOfWords;
        int resultWords = (dims.TargetBits + 31) / 32;

        // The carry chunk of every product is one block of threads (see ExecuteResolveNttCarriesBlocked).
        int chunkSize = BlockDimension.X;

        for (int i = 0; i < dims.Iterations; i++)
        {
            // 1. currentProduct = targetOperand * resultBuffer (n, resultWords -> n + resultWords)
            ExecuteNttMultiplyMacro(core, ops.TargetOperand, ops.ResultBuffer,
                iterScratch.CurrentProduct, n, resultWords, n + resultWords, chunkSize);

            // 2. diff = constant - currentProduct
            Gpu.SyncGrid();
            Gpu.SingleThread(() =>
            {
                var subCtx = new GpuBigIntContext
                {
                    Operands = new OperandContext { CarryBuffer = core.CarryBuffer },
                    NumberOfWords = n + resultWords
                };
                GpuBigIntKernels.ExecuteSub(ref subCtx, iterScratch.Diff, ops.Constant,
                    iterScratch.CurrentProduct);
            });
            Gpu.SyncGrid();

            // 3. nextIter = resultBuffer * diff (resultWords, n + resultWords -> n + 2 * resultWords)
            ExecuteNttMultiplyMacro(core, ops.ResultBuffer, iterScratch.Diff, iterScratch.NextIter,
                resultWords, n + resultWords, n + 2 * resultWords, chunkSize);

            // 4. copy resultBuffer = nextIter >> (2 * numBits + marginBits)
            int shiftTotal = dims.TargetBits;
            int wordShift = shiftTotal / 32;
            int bitShift = shiftTotal % 32;
            int destWords = resultWords;
            int srcMaxWords = n + 2 * resultWords;

            Gpu.GridStrideLoop(destWords, idx =>
            {
                int srcIdx = idx + wordShift;
                // Ensure we don't read out of bounds.
                uint w0 = (srcIdx < srcMaxWords) ? iterScratch.NextIter[srcIdx] : 0u;
                uint w1 = (srcIdx + 1 < srcMaxWords) ? iterScratch.NextIter[srcIdx + 1] : 0u;

                uint shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
                ops.ResultBuffer[idx] = shiftedValue;
            });
            Gpu.SyncGrid();
        }

        // Final correction to x = floor(2^T / b): r = 2^T - b * x, then r moves into [0, b) and x by the same steps.
        // The Newton result is floor(2^T / b) or one less, so this takes at most one step after convergence.
        ExecuteNttMultiplyMacro(core, ops.TargetOperand, ops.ResultBuffer, iterScratch.CurrentProduct, n, resultWords,
            n + resultWords, chunkSize);

        // Constant holds 2^(T+1) in n + 2 * resultWords words; reading it shifted right by one bit gives 2^T.
        int remainderWords = n + resultWords;
        GpuBigIntKernels.DeviceAddSubAlignedParallel(iterScratch.CurrentProduct, remainderWords, ops.Constant,
            n + 2 * resultWords, iterScratch.Diff, remainderWords, 0, 1, -2, true);
        var delta = CorrectIntoDivisorRange(iterScratch.Diff, ops.TargetOperand, remainderWords, n, 1000,
            core.CorrectionStatus, ReciprocalCorrectionFailed);
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
