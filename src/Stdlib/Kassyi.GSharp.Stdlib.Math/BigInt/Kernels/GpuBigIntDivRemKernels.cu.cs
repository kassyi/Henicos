// ReSharper disable All

#pragma warning disable GSH0014

using Kassyi.GSharp.Stdlib.Math.Contexts;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntDivRemKernels
{
    [GpuDeviceFunction]
    private static void ExecuteNttMultiplyMacro(
        ReciprocalNewtonRaphsonContext ctx,
        uint[] opA, uint[] opB, uint[] resultDest,
        int numWordsA, int numWordsB, int numWordsResult)
    {
        var m = ctx.Ntt.TransformLength;
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
            GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, ctx.Workspace.BlockCarries, numWordsResult,
                1024, numBlocks);
        });
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    public static void ReciprocalNewtonRaphsonMegaKernel(ReciprocalNewtonRaphsonContext ctx)
    {
        int n = ctx.Dimensions.NumberOfWords;
        int resultWords = (ctx.Dimensions.TargetBits + 31) / 32;

        for (int i = 0; i < ctx.Dimensions.Iterations; i++)
        {
            // 1. currentProduct = targetOperand * resultBuffer (n, resultWords -> n + resultWords)
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.TargetOperand, ctx.Operands.ResultBuffer,
                ctx.Workspace.CurrentProduct, n, resultWords, n + resultWords);

            // 2. diff = constant - currentProduct
            Gpu.SyncGrid();
            var subCtx = new BigIntKernelContext
            {
                Operands = new BigIntKernelContext.OperandContext { CarryBuffer = ctx.CarryBuffer },
                NumberOfWords = n + resultWords
            };
            GpuBigIntKernels.ExecuteSub(ref subCtx, ctx.Workspace.Diff, ctx.Operands.Constant,
                ctx.Workspace.CurrentProduct);
            Gpu.SyncGrid();

            // 3. nextIter = resultBuffer * diff (resultWords, n + resultWords -> n + 2 * resultWords)
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Workspace.Diff, ctx.Workspace.NextIter,
                resultWords, n + resultWords, n + 2 * resultWords);

            // 4. copy resultBuffer = nextIter >> (2 * numBits + marginBits)
            int shiftTotal = ctx.Dimensions.TargetBits;
            int wordShift = shiftTotal / 32;
            int bitShift = shiftTotal % 32;
            int destWords = resultWords;
            int srcMaxWords = n + 2 * resultWords;

            Gpu.GridStrideLoop(destWords, idx =>
            {
                int srcIdx = idx + wordShift;
                // Ensure we don't read out of bounds.
                uint w0 = (srcIdx < srcMaxWords) ? ctx.Workspace.NextIter[srcIdx] : 0u;
                uint w1 = (srcIdx + 1 < srcMaxWords) ? ctx.Workspace.NextIter[srcIdx + 1] : 0u;

                uint shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
                ctx.Operands.ResultBuffer[idx] = shiftedValue;
            });
            Gpu.SyncGrid();
        }
    }
}
