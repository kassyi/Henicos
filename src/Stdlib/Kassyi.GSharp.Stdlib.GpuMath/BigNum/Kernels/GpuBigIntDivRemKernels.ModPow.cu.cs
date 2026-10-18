
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntDivRemKernels
{
    [GpuDeviceFunction]
    private static void ExecuteNttMultiplyMacroModPow(
        NewtonRaphsonContext core,
        uint[] opA, uint[] opB, uint[] resultDest,
        int numWordsA, int numWordsB, int numWordsResult)
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

        GpuBigIntFermatKernels.DeviceUnpack16(opA, nttScratch.NttBuf1A, nttScratch.NttBuf2A, m, numWordsA, true);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, nttScratch.NttBuf1B, nttScratch.NttBuf2B, m, numWordsB, true);
        Gpu.SyncGrid();

        var baseNtt = new DualNtt
        {
            TemporaryBuffer = nttScratch.TmpBuf,
            Grid = new NttGridContext { M = m, N1 = n1, N2 = n2 }
        };

        var forwardA = baseNtt;
        forwardA.Buffer1 = nttScratch.NttBuf1A;
        forwardA.Buffer2 = nttScratch.NttBuf2A;
        forwardA.Twiddle1 = core.Ntt.TwiddleFactors.Forward1;
        forwardA.Twiddle2 = core.Ntt.TwiddleFactors.Forward2;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardA);

        var forwardB = forwardA;
        forwardB.Buffer1 = nttScratch.NttBuf1B;
        forwardB.Buffer2 = nttScratch.NttBuf2B;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardB);

        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(nttScratch.NttBuf1A, nttScratch.NttBuf2A,
            nttScratch.NttBuf1B, nttScratch.NttBuf2B, m);
        Gpu.SyncGrid();

        var inverseA = baseNtt;
        inverseA.Buffer1 = nttScratch.NttBuf1A;
        inverseA.Buffer2 = nttScratch.NttBuf2A;
        inverseA.Twiddle1 = core.Ntt.TwiddleFactors.Inverse1;
        inverseA.Twiddle2 = core.Ntt.TwiddleFactors.Inverse2;
        GpuBigIntNttKernels.ExecuteDualInverseNtt(inverseA);

        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(nttScratch.NttBuf1A, nttScratch.NttBuf2A,
            nttScratch.UncarriedLow, nttScratch.UncarriedHigh, m);
        Gpu.SyncGrid();

        int chunkSize = BlockDimension.X;
        int numBlocks = (numWordsResult + chunkSize - 1) / chunkSize;

        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(nttScratch.UncarriedLow, nttScratch.UncarriedHigh,
            resultDest, nttScratch.BlockCarries, m, numWordsResult, chunkSize);

        Gpu.SyncGrid();

        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, nttScratch.BlockCarries, numWordsResult,
            chunkSize, numBlocks);

        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    private static void ExecuteBarrettReductionMacro(
        NewtonRaphsonContext core,
        uint[] leftOperand, uint[] rightOperand, uint[] reciprocalOfB,
        int targetBits, int aActualWords, uint[] quotientBuffer, uint[] remainderBuffer)
    {
        var nA = aActualWords;
        var n = core.Dimensions.NumberOfWords;
        var targetWords = (targetBits + 31) / 32;
        var iterScratch = core.Workspace.Iteration;

        ExecuteNttMultiplyMacroModPow(core, leftOperand, reciprocalOfB, iterScratch.CurrentProduct, nA, targetWords,
            nA + targetWords);

        int wordShift = targetBits / 32;
        int bitShift = targetBits % 32;
        int destWords = nA;

        Gpu.GridStrideLoop(destWords, idx =>
        {
            int srcIdx = idx + wordShift;
            uint w0 = (srcIdx < nA + targetWords) ? iterScratch.CurrentProduct[srcIdx] : 0u;
            uint w1 = (srcIdx + 1 < nA + targetWords) ? iterScratch.CurrentProduct[srcIdx + 1] : 0u;
            uint shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
            quotientBuffer[idx] = shiftedValue;
        });
        Gpu.SyncGrid();

        ExecuteNttMultiplyMacroModPow(core, quotientBuffer, rightOperand, iterScratch.NextIter, destWords, n,
            destWords + n);

        int evalWords = aActualWords + 1;
        var subCtx = new GpuBigIntContext
        {
            Operands = new OperandContext { CarryBuffer = core.CarryBuffer },
            NumberOfWords = evalWords
        };

        Gpu.GridStrideLoop(evalWords, idx =>
        {
            iterScratch.Diff[idx] = idx < nA ? leftOperand[idx] : 0u;
        });
        Gpu.SyncGrid();

        GpuBigIntKernels.DeviceAddSubParallel(iterScratch.Diff, iterScratch.NextIter, iterScratch.Diff, evalWords,
            -1, true);
        Gpu.SyncGrid();

        for (int iter = 0; iter < 1000; iter++)
        {
            uint topWord = iterScratch.Diff[evalWords - 1];
            bool underflow = (topWord & 0x80000000u) != 0;

            // Wait for all threads to be ready
            Gpu.SyncGrid();

            if (underflow)
            {
                GpuBigIntKernels.DeviceAddSubParallel(iterScratch.Diff, rightOperand, iterScratch.Diff, evalWords,
                    1, true);
                Gpu.SyncGrid();
            }
            else
            {
                // Try subtracting M to see if R >= M
                GpuBigIntKernels.DeviceAddSubParallel(iterScratch.Diff, rightOperand, iterScratch.Diff, evalWords,
                    -1, true);
                Gpu.SyncGrid();

                uint top2 = iterScratch.Diff[evalWords - 1];
                bool underflow2 = (top2 & 0x80000000u) != 0;
                Gpu.SyncGrid();

                if (underflow2)
                {
                    // Was smaller than M, add back and we are done.
                    GpuBigIntKernels.DeviceAddSubParallel(iterScratch.Diff, rightOperand, iterScratch.Diff,
                        evalWords, 1, true);
                    Gpu.SyncGrid();
                    break;
                }
            }
        }

        Gpu.GridStrideLoop(aActualWords, idx =>
        {
            remainderBuffer[idx] = idx < n ? iterScratch.Diff[idx] : 0u;
        });
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    public static void ModPowNttMegaKernel(ModPowNttContext ctx)
    {
        var core = ctx.Core;
        var iterScratch = core.Workspace.Iteration;
        var expSlice = ctx.Exponent;
        int n = core.Dimensions.NumberOfWords;
        int expWords = expSlice.TotalWords;
        int targetBits = core.Dimensions.TargetBits;
        int maxABits = 2 * n * 32;
        int aActualWords = (maxABits + 31) / 32;
        int targetWords = (targetBits + 31) / 32;
        int outWords = aActualWords + targetWords;

        // Initialize tempResult = 1, tempBase = Base, ModulusExpanded = Constant
        if (expSlice.StartWord == 0)
        {
            Gpu.GridStrideLoop(outWords, idx =>
            {
                if (idx < 2 * n)
                {
                    iterScratch.AxSquared[idx] = idx == 0 ? 1u : 0u; // TempResult
                    iterScratch.XSquared[idx] = idx < n ? core.Operands.TargetOperand[idx] : 0u; // TempBase
                }

                iterScratch.ModulusExpanded[idx] = idx < n ? core.Operands.Constant[idx] : 0u;
            });
            Gpu.SyncGrid();
        }

        for (int i = expSlice.StartWord; i < expSlice.EndWord; i++)
        {
            uint e = expSlice.Words[i];

            if (e == 0)
            {
                // Optimization: check remaining bits using block sync logic omitted for simplicity in kernel
                // We'll just loop 32 times if e == 0 but not all zeros, or we can just process it.
                // To avoid complex block-sync break, just process zero bits (multiply by 1 essentially or just square base).
            }

            for (int bit = 0; bit < 32; bit++)
            {
                if ((e & 1) == 1)
                {
                    ExecuteNttMultiplyMacroModPow(core, iterScratch.AxSquared, iterScratch.XSquared,
                        iterScratch.NextXUnshifted, 2 * n, 2 * n, 4 * n);
                    ExecuteBarrettReductionMacro(core, iterScratch.NextXUnshifted, iterScratch.ModulusExpanded,
                        ctx.ReciprocalMod, targetBits, aActualWords, iterScratch.NextXShifted,
                        iterScratch.AxSquared);
                }

                ExecuteNttMultiplyMacroModPow(core, iterScratch.XSquared, iterScratch.XSquared,
                    iterScratch.NextXUnshifted, 2 * n, 2 * n, 4 * n);
                ExecuteBarrettReductionMacro(core, iterScratch.NextXUnshifted, iterScratch.ModulusExpanded,
                    ctx.ReciprocalMod, targetBits, aActualWords, iterScratch.NextXShifted, iterScratch.XSquared);

                e >>= 1;
            }
        }

        // Copy final result
        if (expSlice.EndWord == expWords)
        {
            Gpu.GridStrideLoop(n, idx =>
            {
                core.Operands.ResultBuffer[idx] = iterScratch.AxSquared[idx];
            });
            Gpu.SyncGrid();
        }
    }
}
