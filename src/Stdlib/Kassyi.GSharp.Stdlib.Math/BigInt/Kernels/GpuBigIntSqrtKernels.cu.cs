// ReSharper disable All

#pragma warning disable GSH0014

using Kassyi.GSharp.Stdlib.Math.Contexts;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntSqrtKernels
{
    [GpuDeviceFunction]
    private static void ExecuteNttMultiplyMacro(
        SqrtNewtonRaphsonContext ctx,
        uint[] opA, uint[] opB, uint[] resultDest,
        int numWordsA, int numWordsB, int numWordsResult)
    {
        var m = ctx.Ntt.TransformLength;
        // Basic parameters for NTT
        int n1 = 32;
        int n2 = m / 32;
        if (m <= 512)
        {
            n1 = m;
            n2 = 1;
        }

        // 1. Unpack
        GpuBigIntFermatKernels.DeviceUnpack16(opA, ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, m, numWordsA, true);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m, numWordsB, true);
        Gpu.SyncGrid();

        // 2. Forward NTT A (Prime 1 & 2)
        GpuBigIntNttKernels.ExecuteDualForwardNtt(
            ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, ctx.Workspace.TmpBuf,
            ctx.Ntt.TwiddleFactors.Forward1, ctx.Ntt.TwiddleFactors.Forward2, m, n1, n2);

        // 3. Forward NTT B (Prime 1 & 2)
        GpuBigIntNttKernels.ExecuteDualForwardNtt(
            ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, ctx.Workspace.TmpBuf,
            ctx.Ntt.TwiddleFactors.Forward1, ctx.Ntt.TwiddleFactors.Forward2, m, n1, n2);

        // 4. Pointwise Multiply
        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A,
            ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m);
        Gpu.SyncGrid();

        // 5. Inverse NTT (Prime 1 & 2)
        GpuBigIntNttKernels.ExecuteDualInverseNtt(
            ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, ctx.Workspace.TmpBuf,
            ctx.Ntt.TwiddleFactors.Inverse1, ctx.Ntt.TwiddleFactors.Inverse2, m, n1, n2);

        // 6. Compute CRT
        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A,
            ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh, m);
        Gpu.SyncGrid();

        // 7-8. Resolve Carries (Blocked & Apply)
        int numBlocks = (numWordsResult + 1023) / 1024;
        Gpu.SyncGrid(() =>
        {
            GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh,
                resultDest, ctx.Workspace.BlockCarries, m, numWordsResult, 1024);
            GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, ctx.Workspace.BlockCarries, numWordsResult, 1024,
                numBlocks);
        });
        Gpu.SyncGrid();
    }

    /// <summary>
    ///Newton-Raphson Mega Kernel (Inverse Sqrt)
    /// </summary>
    [GpuKernel(Cooperative = true)]
    public static void InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphsonContext ctx)
    {
        int n = ctx.Dimensions.NumberOfWords;

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
                var subCtx = new BigIntKernelContext
                {
                    Operands = new BigIntKernelContext.OperandContext { CarryBuffer = ctx.CarryBuffer },
                    NumberOfWords = 3 * n + 2 * ctx.Dimensions.MarginWords
                };
                GpuBigIntKernels.ExecuteSub(ref subCtx, ctx.Workspace.Diff, ctx.Operands.Constant3,
                    ctx.Workspace.AxSquared);
            });
            Gpu.SyncGrid();

            // 4. nextXUnshifted = resultBuffer * diff
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Workspace.Diff, ctx.Workspace.NextXUnshifted,
                n + ctx.Dimensions.MarginWords, 3 * n + 2 * ctx.Dimensions.MarginWords, 4 * n + 3 * ctx.Dimensions.MarginWords);

            // 5. resultBuffer = nextXUnshifted >> (2 * n * 32 + 2 * marginBits + 1)
            int shiftTotal = 2 * n * 32 + 2 * ctx.Dimensions.MarginBits + 1;
            int wordShift = shiftTotal / 32;
            int bitShift = shiftTotal % 32;
            int destWords = n + ctx.Dimensions.MarginWords;
            int srcMaxWords = 4 * n + 3 * ctx.Dimensions.MarginWords;

            Gpu.GridStrideLoop(destWords, idx =>
            {
                int srcIdx = idx + wordShift;
                // Ensure we don't read out of bounds. The nextXUnshifted buffer size is 4n+3m
                uint w0 = (srcIdx < srcMaxWords) ? ctx.Workspace.NextXUnshifted[srcIdx] : 0u;
                uint w1 = (srcIdx + 1 < srcMaxWords) ? ctx.Workspace.NextXUnshifted[srcIdx + 1] : 0u;

                uint shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
                ctx.Operands.ResultBuffer[idx] = shiftedValue;
            });
            Gpu.SyncGrid();
        }
    }
}
