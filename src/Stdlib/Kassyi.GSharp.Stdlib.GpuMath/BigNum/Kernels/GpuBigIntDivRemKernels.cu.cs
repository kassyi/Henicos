// ReSharper disable All

#pragma warning disable GSH0014

using Kassyi.GSharp.Stdlib.GpuMath.Contexts;


namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntDivRemKernels
{
    // Dummy kernel to force the generator to emit the DualNtt struct definition before we use it
    [GpuDeviceFunction]
    private static void ForceEmitDualNtt_DivRem(DualNtt args)
    {
    }

    [GpuDeviceFunction]
    private static void ExecuteNttMultiplyMacro(
        ReciprocalNewtonRaphson ctx,
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
        int numBlocks = (numWordsResult + 255) / 256;
        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh,
            resultDest, ctx.Workspace.BlockCarries, m, numWordsResult, 256);
        Gpu.SyncGrid();
        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, ctx.Workspace.BlockCarries, numWordsResult,
            256, numBlocks);
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    public static void ReciprocalNewtonRaphsonMegaKernel(ReciprocalNewtonRaphson ctx)
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
            var subCtx = new GpuBigIntContext
            {
                Operands = new OperandContext { CarryBuffer = ctx.CarryBuffer },
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
