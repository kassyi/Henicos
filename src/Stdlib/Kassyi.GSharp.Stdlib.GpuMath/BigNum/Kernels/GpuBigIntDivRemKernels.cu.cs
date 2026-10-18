#pragma warning disable GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntDivRemKernels
{
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

        for (int i = 0; i < dims.Iterations; i++)
        {
            // 1. currentProduct = targetOperand * resultBuffer (n, resultWords -> n + resultWords)
            ExecuteNttMultiplyMacro(core, ops.TargetOperand, ops.ResultBuffer,
                iterScratch.CurrentProduct, n, resultWords, n + resultWords, 1024);

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
                resultWords, n + resultWords, n + 2 * resultWords, 1024);

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
    }

    [GpuKernel]
    public static void BarrettCorrectionKernel(uint[] diff, uint[] rightOperand, int evalWords)
    {
        for (int iter = 0; iter < 1000; iter++)
        {
            uint topWord = diff[evalWords - 1];
            bool underflow = (topWord & 0x80000000u) != 0;
            Gpu.SyncThreads();

            if (underflow)
            {
                GpuBigIntKernels.DeviceAddSubParallel(diff, rightOperand, diff, evalWords, 1, false);
            }
            else
            {
                GpuBigIntKernels.DeviceAddSubParallel(diff, rightOperand, diff, evalWords, -1, false);
            }

            Gpu.SyncThreads();

            uint top2 = diff[evalWords - 1];
            bool underflow2 = (top2 & 0x80000000u) != 0;
            Gpu.SyncThreads();

            bool done = false;
            if (!underflow && underflow2)
            {
                GpuBigIntKernels.DeviceAddSubParallel(diff, rightOperand, diff, evalWords, 1, false);
                done = true;
            }

            Gpu.SyncThreads();

            if (done)
            {
                break;
            }
        }
    }
}
