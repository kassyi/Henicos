// ReSharper disable All

#pragma warning disable GSH0014

using Kassyi.GSharp.Stdlib.Math.Contexts;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
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
    /// メガカーネルによる Newton-Raphson法 (Inverse Sqrt)
    /// </summary>
    [GpuKernel(Cooperative = true)]
    public static void InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphsonContext ctx)
    {
        var targetOperand = ctx.Operands.TargetOperand;
        var resultBuffer = ctx.Operands.ResultBuffer;
        var constant3 = ctx.Operands.Constant3;
        var numWords = ctx.Dimensions.NumberOfWords;
        var marginWords = ctx.Dimensions.MarginWords;
        var actualBits = ctx.Dimensions.ActualBits;
        var marginBits = ctx.Dimensions.MarginBits;
        var iterations = ctx.Dimensions.Iterations;
        var xSquared = ctx.Workspace.XSquared;
        var axSquared = ctx.Workspace.AxSquared;
        var diff = ctx.Workspace.Diff;
        var nextXUnshifted = ctx.Workspace.NextXUnshifted;
        var nextXShifted = ctx.Workspace.NextXShifted;
        var nttBuf1A = ctx.Workspace.NttBuf1A;
        var nttBuf2A = ctx.Workspace.NttBuf2A;
        var nttBuf1B = ctx.Workspace.NttBuf1B;
        var nttBuf2B = ctx.Workspace.NttBuf2B;
        var tmpBuf = ctx.Workspace.TmpBuf;
        var uncarriedLow = ctx.Workspace.UncarriedLow;
        var uncarriedHigh = ctx.Workspace.UncarriedHigh;
        var blockCarries = ctx.Workspace.BlockCarries;
        var carryBuffer = ctx.CarryBuffer;
        var twiddleF1 = ctx.Ntt.TwiddleFactors.Forward1;
        var twiddleF2 = ctx.Ntt.TwiddleFactors.Forward2;
        var twiddleI1 = ctx.Ntt.TwiddleFactors.Inverse1;
        var twiddleI2 = ctx.Ntt.TwiddleFactors.Inverse2;
        var transformLength = ctx.Ntt.TransformLength;

        int n = numWords;

        // ニュートン・ラフソン法のメインループ
        for (int i = 0; i < iterations; i++)
        {
            // 1. xSquared = resultBuffer * resultBuffer
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Operands.ResultBuffer, ctx.Workspace.XSquared,
                n + marginWords, n + marginWords, 2 * n + 2 * marginWords);

            // 2. axSquared = targetOperand * xSquared
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.TargetOperand, ctx.Workspace.XSquared, ctx.Workspace.AxSquared, n,
                2 * n + 2 * marginWords, 3 * n + 2 * marginWords);

            // 3. diff = constant3 - axSquared
            Gpu.SingleThread(() =>
            {
                var subCtx = new BigIntKernelContext
                {
                    Operands = new BigIntKernelContext.OperandContext { CarryBuffer = ctx.CarryBuffer },
                    NumberOfWords = 3 * n + 2 * marginWords
                };
                GpuBigIntKernels.ExecuteSub(ref subCtx, ctx.Workspace.Diff, ctx.Operands.Constant3,
                    ctx.Workspace.AxSquared);
            });
            Gpu.SyncGrid();

            // 4. nextXUnshifted = resultBuffer * diff
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Workspace.Diff, ctx.Workspace.NextXUnshifted,
                n + marginWords, 3 * n + 2 * marginWords, 4 * n + 3 * marginWords);

            // 5. resultBuffer = nextXUnshifted >> (2 * n * 32 + 2 * marginBits + 1)
            int shiftTotal = 2 * n * 32 + 2 * ctx.Dimensions.MarginBits + 1;
            int wordShift = shiftTotal / 32;
            int bitShift = shiftTotal % 32;
            int destWords = n + marginWords;
            int srcMaxWords = 4 * n + 3 * marginWords;

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
