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

    [GpuDeviceFunction]
    private static void ExecuteNttMultiplyMacro(
        SqrtNewtonRaphson ctx,
        uint[] opA, uint[] opB, uint[] resultDest,
        int numWordsA, int numWordsB, int numWordsResult)
    {
        var m = ctx.Ntt.TransformLength;
        // Basic parameters for NTT
        int n1 = 32;
        var n2 = m / 32;
        if (m <= 512)
        {
            n1 = m;
            n2 = 1;
        }

        // 1. Unpack
        GpuBigIntFermatKernels.DeviceUnpack16(opA, ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, m, numWordsA);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m, numWordsB);
        Gpu.SyncGrid();

        // 2. Base NTT Context
        var baseNtt = new DualNtt
        {
            TemporaryBuffer = ctx.Workspace.TmpBuf,
            M = m,
            N1 = n1,
            N2 = n2
        };

        // 3. Forward NTT A (Prime 1 & 2)
        var forwardA = baseNtt;
        forwardA.Buffer1 = ctx.Workspace.NttBuf1A;
        forwardA.Buffer2 = ctx.Workspace.NttBuf2A;
        forwardA.Twiddle1 = ctx.Ntt.TwiddleFactors.Forward1;
        forwardA.Twiddle2 = ctx.Ntt.TwiddleFactors.Forward2;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardA);

        // 4. Forward NTT B (Prime 1 & 2)
        var forwardB = forwardA; // Twiddles are the same, just update buffers
        forwardB.Buffer1 = ctx.Workspace.NttBuf1B;
        forwardB.Buffer2 = ctx.Workspace.NttBuf2B;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardB);

        // 5. Pointwise Multiply
        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A,
            ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m);
        Gpu.SyncGrid();

        // 6. Inverse NTT (Prime 1 & 2)
        var inverseA = baseNtt;
        inverseA.Buffer1 = ctx.Workspace.NttBuf1A;
        inverseA.Buffer2 = ctx.Workspace.NttBuf2A;
        inverseA.Twiddle1 = ctx.Ntt.TwiddleFactors.Inverse1;
        inverseA.Twiddle2 = ctx.Ntt.TwiddleFactors.Inverse2;
        GpuBigIntNttKernels.ExecuteDualInverseNtt(inverseA);

        // 6. Compute CRT
        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A,
            ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh, m);
        Gpu.SyncGrid();

        // 7-8. Resolve Carries (Blocked & Apply)
        var numBlocks = (numWordsResult + 255) / 256;
        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh,
            resultDest, ctx.Workspace.BlockCarries, m, numWordsResult, 256);
        Gpu.SyncGrid();
        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, ctx.Workspace.BlockCarries, numWordsResult,
            256, numBlocks);
        Gpu.SyncGrid();
    }

    /// <summary>
    ///Newton-Raphson Mega Kernel (Inverse Sqrt)
    /// </summary>
    [GpuKernel(Cooperative = true)]
    public static void InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphson ctx)
    {
        var n = ctx.Dimensions.NumberOfWords;

        // main loop
        for (int i = 0; i < ctx.Dimensions.Iterations; i++)
        {
            // 1. xSquared = resultBuffer * resultBuffer
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Operands.ResultBuffer, ctx.Workspace.XSquared,
                n + ctx.Dimensions.MarginWords, n + ctx.Dimensions.MarginWords, 2 * n + 2 * ctx.Dimensions.MarginWords);

            // 2. axSquared = targetOperand * xSquared
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.TargetOperand, ctx.Workspace.XSquared, ctx.Workspace.AxSquared, n,
                2 * n + 2 * ctx.Dimensions.MarginWords, 3 * n + 2 * ctx.Dimensions.MarginWords);

            // 3. diff = constant3 - axSquared
            Gpu.SingleThread(() =>
            {
                var subCtx = new GpuBigIntContext
                {
                    Operands = new OperandContext { CarryBuffer = ctx.CarryBuffer },
                    NumberOfWords = 3 * n + 2 * ctx.Dimensions.MarginWords
                };
                GpuBigIntKernels.ExecuteSub(ref subCtx, ctx.Workspace.Diff, ctx.Operands.Constant3,
                    ctx.Workspace.AxSquared);
            });
            Gpu.SyncGrid();

            // 4. nextXUnshifted = resultBuffer * diff
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Workspace.Diff, ctx.Workspace.NextXUnshifted,
                n + ctx.Dimensions.MarginWords, 3 * n + 2 * ctx.Dimensions.MarginWords,
                4 * n + 3 * ctx.Dimensions.MarginWords);

            // 5. resultBuffer = nextXUnshifted >> (2 * n * 32 + 2 * marginBits + 1)
            var shiftTotal = 2 * n * 32 + 2 * ctx.Dimensions.MarginBits + 1;
            var wordShift = shiftTotal / 32;
            var bitShift = shiftTotal % 32;
            var destWords = n + ctx.Dimensions.MarginWords;
            var srcMaxWords = 4 * n + 3 * ctx.Dimensions.MarginWords;

            Gpu.GridStrideLoop(destWords, idx =>
            {
                var srcIdx = idx + wordShift;
                // Ensure we don't read out of bounds. The nextXUnshifted buffer size is 4n+3m
                var w0 = (srcIdx < srcMaxWords) ? ctx.Workspace.NextXUnshifted[srcIdx] : 0u;
                var w1 = (srcIdx + 1 < srcMaxWords) ? ctx.Workspace.NextXUnshifted[srcIdx + 1] : 0u;

                var shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
                ctx.Operands.ResultBuffer[idx] = shiftedValue;
            });
            Gpu.SyncGrid();
        }
    }
}
