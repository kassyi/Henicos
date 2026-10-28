namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntSqrtKernels
{
    // Dummy kernel to force the generator to emit the DualNtt struct definition before we use it
    [GpuDeviceFunction]
    private static void ForceEmitDualNtt_Sqrt(DualNtt args)
    {
    }

    /// <summary>
    ///Newton-Raphson Mega Kernel (Inverse Sqrt)
    /// </summary>
    [GpuKernel(Cooperative = true)]
    public static void InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphson ctx)
    {
        var core = ctx.Core;
        var dims = core.Dimensions;
        var ops = core.Operands;
        var iterScratch = core.Workspace.Iteration;
        var n = dims.NumberOfWords;

        // The carry chunk of every product is one block of threads: ExecuteResolveNttCarriesBlocked gives each thread
        // of a block one word of a chunk, so a chunk smaller than the block overlaps the blocks and corrupts products
        // longer than one chunk. The host launches 1024 threads and sizes the block-carry buffer for 1024-word chunks.
        int chunkSize = BlockDimension.X;

        // main loop
        for (int i = 0; i < dims.Iterations; i++)
        {
            // 1. xSquared = resultBuffer * resultBuffer
            GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.ResultBuffer, ops.ResultBuffer, iterScratch.XSquared,
                n + dims.MarginWords, n + dims.MarginWords, 2 * n + 2 * dims.MarginWords, chunkSize);

            // 2. axSquared = targetOperand * xSquared
            GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.TargetOperand, iterScratch.XSquared, iterScratch.AxSquared, n,
                2 * n + 2 * dims.MarginWords, 3 * n + 2 * dims.MarginWords, chunkSize);

            // 3. diff = constant3 - axSquared
            Gpu.SingleThread(() =>
            {
                var subCtx = new GpuBigIntContext
                {
                    Operands = new OperandContext { CarryBuffer = core.CarryBuffer },
                    NumberOfWords = 3 * n + 2 * dims.MarginWords
                };
                GpuBigIntKernels.ExecuteSub(ref subCtx, iterScratch.Diff, ops.Constant3,
                    iterScratch.AxSquared);
            });
            Gpu.SyncGrid();

            // 4. nextXUnshifted = resultBuffer * diff
            GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.ResultBuffer, iterScratch.Diff, iterScratch.NextXUnshifted,
                n + dims.MarginWords, 3 * n + 2 * dims.MarginWords,
                4 * n + 3 * dims.MarginWords, chunkSize);

            // 5. resultBuffer = nextXUnshifted >> (2 * n * 32 + 2 * marginBits + 1)
            var shiftTotal = 2 * n * 32 + 2 * dims.MarginBits + 1;
            var wordShift = shiftTotal / 32;
            var bitShift = shiftTotal % 32;
            var destWords = n + dims.MarginWords;
            var srcMaxWords = 4 * n + 3 * dims.MarginWords;

            Gpu.GridStrideLoop(destWords, idx =>
            {
                var srcIdx = idx + wordShift;
                // Ensure we don't read out of bounds. The nextXUnshifted buffer size is 4n+3m
                var w0 = (srcIdx < srcMaxWords) ? iterScratch.NextXUnshifted[srcIdx] : 0u;
                var w1 = (srcIdx + 1 < srcMaxWords) ? iterScratch.NextXUnshifted[srcIdx + 1] : 0u;

                var shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
                ops.ResultBuffer[idx] = shiftedValue;
            });
            Gpu.SyncGrid();
        }

        // Final correction to x = floor(2^B / sqrt(a)) with B = (n + marginWords) * 32, the largest x with
        // a * x^2 <= 2^(2B): s = 2^(2B) - a * x^2 is updated by a * (2x -/+ 1) per step, so only additions follow the
        // three products below.
        var xWords = n + dims.MarginWords;
        var squareWords = 2 * xWords;
        var slackWords = 3 * n + 2 * dims.MarginWords;
        var axWords = 2 * n + dims.MarginWords;
        GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.ResultBuffer, ops.ResultBuffer, iterScratch.XSquared,
            xWords, xWords, squareWords, chunkSize);
        GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.TargetOperand, iterScratch.XSquared,
            iterScratch.AxSquared, n, squareWords, slackWords, chunkSize);
        GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.TargetOperand, ops.ResultBuffer,
            iterScratch.NextXUnshifted, n, xWords, axWords, chunkSize);

        // s = 2^(2B) - a * x^2, with 2^(2B) (bit 0 of word 2 * xWords) written into Diff first.
        Gpu.GridStrideLoop(slackWords, idx =>
        {
            iterScratch.Diff[idx] = idx == squareWords ? 1u : 0u;
        });
        Gpu.SyncGrid();
        GpuBigIntKernels.DeviceAddSubAlignedParallel(iterScratch.Diff, slackWords, iterScratch.AxSquared, slackWords,
            iterScratch.Diff, slackWords, 0, 0, -1, true);

        var delta = CorrectInverseSqrtSlack(iterScratch.Diff, iterScratch.NextXUnshifted, ops.TargetOperand,
            iterScratch.NextXShifted, slackWords, axWords, n, 1000, core.CorrectionStatus,
            GpuBigIntDivRemKernels.InverseSqrtCorrectionFailed);
        Gpu.SingleThread(() => GpuBigIntDivRemKernels.AddSmallSigned(ops.ResultBuffer, xWords, delta));
    }

    /// <summary>
    /// Moves an integer square root estimate <c>r</c> of <c>a</c> to <c>floor(sqrt(a))</c>, one step at a time, by
    /// updating the slack <c>s = a - r^2</c>: <c>r</c> is the floor exactly when <c>0 &lt;= s &lt; 2r + 1</c>.
    /// </summary>
    /// <param name="operand">The operand <c>a</c> (<paramref name="operandWords"/> words).</param>
    /// <param name="rootSquared">The square <c>r^2</c> of the estimate (<paramref name="operandWords"/> words).</param>
    /// <param name="root">The estimate <c>r</c> (<paramref name="rootWords"/> words), corrected in place.</param>
    /// <param name="slack">Scratch for the signed slack <c>s</c> (two's complement over <c>operandWords + 1</c> words).</param>
    /// <param name="trial">Scratch for the slack of <c>r + 1</c> (<c>operandWords + 1</c> words).</param>
    /// <param name="operandWords">The word count of <paramref name="operand"/> and <paramref name="rootSquared"/>.</param>
    /// <param name="rootWords">The word count of <paramref name="root"/>; <c>2r + 1</c> must fit in
    /// <c>operandWords + 1</c> words.</param>
    /// <param name="maxSteps">The upper bound on the number of steps.</param>
    /// <param name="status">The correction status words; thread 0 adds 1 to <c>status[0]</c> and writes
    /// <see cref="GpuBigIntDivRemKernels.SqrtCorrectionFailed"/> to <c>status[1]</c> when <c>r</c> is not confirmed within
    /// <paramref name="maxSteps"/> steps.</param>
    /// <remarks>
    /// Launched with one block; runs on the stream without a host synchronization. Stepping down (<c>s &lt; 0</c>):
    /// <c>s += 2r - 1</c>, <c>r -= 1</c>. Stepping up: <c>s -= 2r + 1</c>, <c>r += 1</c>, kept only while the new slack
    /// is non-negative. Every thread reads the same sign words after a barrier and takes the same branches.
    /// </remarks>
    [GpuKernel]
    public static void SqrtFloorCorrectionKernel([GpuReadOnly] uint[] operand, [GpuReadOnly] uint[] rootSquared,
        uint[] root, uint[] slack, uint[] trial, int operandWords, int rootWords, int maxSteps, uint[] status)
    {
        // Launched with one block; any other block would race on the same words.
        if (BlockIndex.X != 0)
        {
            return;
        }

        var slackWords = operandWords + 1;

        // s = a - r^2.
        GpuBigIntKernels.DeviceAddSubAlignedParallel(operand, operandWords, rootSquared, operandWords, slack,
            slackWords, 0, 0, -1, true);

        var converged = false;

        // At most maxSteps moves; the pass after the last move only checks, so an r exactly maxSteps steps away is still
        // confirmed.
        for (int step = 0; step <= maxSteps; step++)
        {
            var negative = (slack[slackWords - 1] & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (negative)
            {
                if (step == maxSteps)
                {
                    break;
                }

                // r^2 > a: r -> r - 1, s += 2r - 1. Reading root with word shift -1 and bit shift 31 gives 2r.
                GpuBigIntKernels.DeviceAddSubAlignedParallel(slack, slackWords, root, rootWords, slack, slackWords, -1,
                    31, 1, true);
                Gpu.SingleThread(() =>
                {
                    GpuBigIntDivRemKernels.AddSmallSigned(slack, slackWords, -1);
                    GpuBigIntDivRemKernels.AddSmallSigned(root, rootWords, -1);
                });
                Gpu.SyncThreads();
                continue;
            }

            // Slack of r + 1: s - 2r - 1.
            GpuBigIntKernels.DeviceAddSubAlignedParallel(slack, slackWords, root, rootWords, trial, slackWords, -1, 31,
                -1, true);
            Gpu.SingleThread(() => GpuBigIntDivRemKernels.AddSmallSigned(trial, slackWords, -1));
            Gpu.SyncThreads();
            var trialNegative = (trial[slackWords - 1] & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (trialNegative || step == maxSteps)
            {
                converged = trialNegative;
                break;
            }

            for (var idx = ThreadIndex.X; idx < slackWords; idx += BlockDimension.X)
            {
                slack[idx] = trial[idx];
            }

            Gpu.SingleThread(() => GpuBigIntDivRemKernels.AddSmallSigned(root, rootWords, 1));
            Gpu.SyncThreads();
        }

        if (!converged && ThreadIndex.X == 0)
        {
            // status[0] counts the failures (it only grows) and status[1] keeps the code of the last one.
            Gpu.AtomicAdd(ref status[0], 1u);
            status[1] = GpuBigIntDivRemKernels.SqrtCorrectionFailed;
        }
    }

    /// <summary>
    /// Moves an inverse square root <c>x</c> to the largest value with <c>a * x^2 &lt;= 2^(2B)</c>, one step at a time,
    /// by updating the slack <c>s = 2^(2B) - a * x^2</c> and the product <c>a * x</c>.
    /// </summary>
    /// <param name="slack">The signed slack <c>s</c> (two's complement over <paramref name="slackWords"/> words).</param>
    /// <param name="ax">The product <c>a * x</c> (<paramref name="axWords"/> words), updated with each step.</param>
    /// <param name="operand">The operand <c>a</c> (<paramref name="operandWords"/> words).</param>
    /// <param name="trial">Scratch for the slack of <c>x + 1</c> (<paramref name="slackWords"/> words).</param>
    /// <param name="slackWords">The word count of <paramref name="slack"/> and <paramref name="trial"/>.</param>
    /// <param name="axWords">The word count of <paramref name="ax"/>.</param>
    /// <param name="operandWords">The word count of <paramref name="operand"/>.</param>
    /// <param name="maxSteps">The upper bound on the number of steps.</param>
    /// <param name="status">The correction status words; thread 0 adds 1 to <c>status[0]</c> and writes
    /// <paramref name="failureCode"/> to <c>status[1]</c> when <c>x</c> is not confirmed within
    /// <paramref name="maxSteps"/> steps.</param>
    /// <param name="failureCode">The status code that names the failed operation.</param>
    /// <returns>The number of steps to add to <c>x</c>.</returns>
    /// <remarks>
    /// Runs on block 0 only (the other blocks return 0 at once); all threads of block 0 read the same sign words after a
    /// barrier and take the same branches. Stepping down: <c>s += 2ax - a</c>, <c>ax -= a</c>. Stepping up:
    /// <c>s -= 2ax + a</c>, <c>ax += a</c>, kept only while the new slack is non-negative.
    /// </remarks>
    [GpuDeviceFunction]
    public static int CorrectInverseSqrtSlack(uint[] slack, uint[] ax, [GpuReadOnly] uint[] operand, uint[] trial,
        int slackWords, int axWords, int operandWords, int maxSteps, uint[] status, uint failureCode)
    {
        if (BlockIndex.X != 0)
        {
            return 0;
        }

        int delta = 0;
        bool converged = false;

        // At most maxSteps moves; the pass after the last move only checks, so an x exactly maxSteps steps away is still
        // confirmed.
        for (int step = 0; step <= maxSteps; step++)
        {
            var negative = (slack[slackWords - 1] & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (negative)
            {
                if (step == maxSteps)
                {
                    break;
                }

                // a * x^2 > 2^(2B): x -> x - 1. Reading ax with word shift -1 and bit shift 31 gives 2 * ax.
                GpuBigIntKernels.DeviceAddSubAlignedParallel(slack, slackWords, ax, axWords, slack, slackWords, -1, 31,
                    1, true);
                GpuBigIntKernels.DeviceAddSubAlignedParallel(slack, slackWords, operand, operandWords, slack,
                    slackWords, 0, 0, -1, true);
                GpuBigIntKernels.DeviceAddSubAlignedParallel(ax, axWords, operand, operandWords, ax, axWords, 0, 0, -1,
                    true);
                delta--;
                continue;
            }

            // Slack of x + 1: s - 2ax - a.
            GpuBigIntKernels.DeviceAddSubAlignedParallel(slack, slackWords, ax, axWords, trial, slackWords, -1, 31, -1,
                true);
            GpuBigIntKernels.DeviceAddSubAlignedParallel(trial, slackWords, operand, operandWords, trial, slackWords,
                0, 0, -1, true);
            var trialNegative = (trial[slackWords - 1] & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (trialNegative || step == maxSteps)
            {
                converged = trialNegative;
                break;
            }

            for (int idx = ThreadIndex.X; idx < slackWords; idx += BlockDimension.X)
            {
                slack[idx] = trial[idx];
            }

            Gpu.SyncThreads();
            GpuBigIntKernels.DeviceAddSubAlignedParallel(ax, axWords, operand, operandWords, ax, axWords, 0, 0, 1,
                true);
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
}
