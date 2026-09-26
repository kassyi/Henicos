// ReSharper disable All
#pragma warning disable GSH0014

using Kassyi.GSharp.Stdlib.Math.Contexts;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
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
        var argsA1 = new NumberTheoreticTransformKernelArgs { Buffer = ctx.Workspace.NttBuf1A, TemporaryBuffer = ctx.Workspace.TmpBuf, Twiddle = ctx.Ntt.TwiddleFactors.Forward1, M = m, N1 = n1, N2 = n2, P = GpuBigIntNttKernels.Prime1, Pm = GpuBigIntNttKernels.BarrettMagic1 };
        GpuBigIntNttKernels.ExecuteForwardNtt(argsA1);

        var argsA2 = new NumberTheoreticTransformKernelArgs { Buffer = ctx.Workspace.NttBuf2A, TemporaryBuffer = ctx.Workspace.TmpBuf, Twiddle = ctx.Ntt.TwiddleFactors.Forward2, M = m, N1 = n1, N2 = n2, P = GpuBigIntNttKernels.Prime2, Pm = GpuBigIntNttKernels.BarrettMagic2 };
        GpuBigIntNttKernels.ExecuteForwardNtt(argsA2);
        Gpu.SyncGrid();

        // 3. Forward NTT B (Prime 1 & 2)
        var argsB1 = new NumberTheoreticTransformKernelArgs { Buffer = ctx.Workspace.NttBuf1B, TemporaryBuffer = ctx.Workspace.TmpBuf, Twiddle = ctx.Ntt.TwiddleFactors.Forward1, M = m, N1 = n1, N2 = n2, P = GpuBigIntNttKernels.Prime1, Pm = GpuBigIntNttKernels.BarrettMagic1 };
        GpuBigIntNttKernels.ExecuteForwardNtt(argsB1);

        var argsB2 = new NumberTheoreticTransformKernelArgs { Buffer = ctx.Workspace.NttBuf2B, TemporaryBuffer = ctx.Workspace.TmpBuf, Twiddle = ctx.Ntt.TwiddleFactors.Forward2, M = m, N1 = n1, N2 = n2, P = GpuBigIntNttKernels.Prime2, Pm = GpuBigIntNttKernels.BarrettMagic2 };
        GpuBigIntNttKernels.ExecuteForwardNtt(argsB2);
        Gpu.SyncGrid();

        // 4. Pointwise Multiply
        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m);
        Gpu.SyncGrid();

        // 5. Inverse NTT (Prime 1 & 2)
        var argsI1 = new NumberTheoreticTransformKernelArgs { Buffer = ctx.Workspace.NttBuf1A, TemporaryBuffer = ctx.Workspace.TmpBuf, Twiddle = ctx.Ntt.TwiddleFactors.Inverse1, M = m, N1 = n1, N2 = n2, P = GpuBigIntNttKernels.Prime1, Pm = GpuBigIntNttKernels.BarrettMagic1, IsInverse = true };
        GpuBigIntNttKernels.ExecuteInverseNtt(argsI1);

        var argsI2 = new NumberTheoreticTransformKernelArgs { Buffer = ctx.Workspace.NttBuf2A, TemporaryBuffer = ctx.Workspace.TmpBuf, Twiddle = ctx.Ntt.TwiddleFactors.Inverse2, M = m, N1 = n1, N2 = n2, P = GpuBigIntNttKernels.Prime2, Pm = GpuBigIntNttKernels.BarrettMagic2, IsInverse = true };
        GpuBigIntNttKernels.ExecuteInverseNtt(argsI2);
        Gpu.SyncGrid();

        // 6. Compute CRT
        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh, m);
        Gpu.SyncGrid();

        // 7. Resolve Carries Blocked
        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh, resultDest, ctx.Workspace.BlockCarries, m, numWordsResult, 1024);
        Gpu.SyncGrid();

        // 8. Resolve Carries Apply
        int numBlocks = (numWordsResult + 1023) / 1024;
        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, ctx.Workspace.BlockCarries, numWordsResult, 1024, numBlocks);
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    public static void ReciprocalNewtonRaphsonMegaKernel(
        uint[] targetOperand, uint[] resultBuffer, uint[] constant,
        uint[] currentProduct, uint[] diff, uint[] nextIter,
        uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, uint[] tmpBuf,
        uint[] uncarriedLow, uint[] uncarriedHigh, uint[] blockCarries,
        long[] carryBuffer,
        uint[] twiddleF1, uint[] twiddleF2, uint[] twiddleI1, uint[] twiddleI2,
        int numWords, int marginWords, int targetBits, int iterations, int transformLength)
    {
        var ctx = new ReciprocalNewtonRaphsonContext
        {
            Operands = new ReciprocalNewtonRaphsonContext.OperandsContext
            {
                TargetOperand = targetOperand,
                ResultBuffer = resultBuffer,
                Constant = constant
            },
            Dimensions = new ReciprocalNewtonRaphsonContext.DimensionsContext
            {
                NumberOfWords = numWords,
                MarginWords = marginWords,
                NumBits = numWords * 32,
                MarginBits = marginWords * 32,
                Iterations = iterations
            },
            Ntt = new NttContext
            {
                TransformLength = transformLength,
                TwiddleFactors = new NttContext.TwiddleFactorsContext
                {
                    Forward1 = twiddleF1,
                    Forward2 = twiddleF2,
                    Inverse1 = twiddleI1,
                    Inverse2 = twiddleI2
                }
            },
            Workspace = new ReciprocalNewtonRaphsonContext.WorkspaceContext
            {
                CurrentProduct = currentProduct,
                Diff = diff,
                NextIter = nextIter,
                NttBuf1A = nttBuf1A,
                NttBuf2A = nttBuf2A,
                NttBuf1B = nttBuf1B,
                NttBuf2B = nttBuf2B,
                TmpBuf = tmpBuf,
                UncarriedLow = uncarriedLow,
                UncarriedHigh = uncarriedHigh,
                BlockCarries = blockCarries
            },
            CarryBuffer = carryBuffer
        };

        int n = numWords;
        int resultWords = (targetBits + 31) / 32;

        for (int i = 0; i < iterations; i++)
        {
            // 1. currentProduct = targetOperand * resultBuffer (n, resultWords -> n + resultWords)
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.TargetOperand, ctx.Operands.ResultBuffer, ctx.Workspace.CurrentProduct, n, resultWords, n + resultWords);

            // 2. diff = constant - currentProduct
            Gpu.SyncGrid();
            var subCtx = new BigIntKernelContext
            {
                Operands = new BigIntKernelContext.OperandsContext { CarryBuffer = ctx.CarryBuffer },
                NumberOfWords = n + resultWords
            };
            GpuBigIntKernels.ExecuteSub(ref subCtx, ctx.Workspace.Diff, ctx.Operands.Constant, ctx.Workspace.CurrentProduct);
            Gpu.SyncGrid();

            // 3. nextIter = resultBuffer * diff (resultWords, n + resultWords -> n + 2 * resultWords)
            ExecuteNttMultiplyMacro(ctx, ctx.Operands.ResultBuffer, ctx.Workspace.Diff, ctx.Workspace.NextIter, resultWords, n + resultWords, n + 2 * resultWords);

            // 4. copy resultBuffer = nextIter >> (2 * numBits + marginBits)
            int shiftTotal = targetBits;
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
