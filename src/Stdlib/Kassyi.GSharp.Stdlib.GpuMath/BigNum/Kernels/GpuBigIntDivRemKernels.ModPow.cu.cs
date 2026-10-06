// ReSharper disable All

#pragma warning disable GSH0014

using Kassyi.GSharp.Stdlib.GpuMath.Contexts;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntDivRemKernels
{
    [GpuDeviceFunction]
    private static void ExecuteNttMultiplyMacroModPow(
        ModPowNttContext ctx,
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

        GpuBigIntFermatKernels.DeviceUnpack16(opA, ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A, m, numWordsA, true);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m, numWordsB, true);
        Gpu.SyncGrid();

        var baseNtt = new DualNtt
        {
            TemporaryBuffer = ctx.Workspace.TmpBuf,
            M = m,
            N1 = n1,
            N2 = n2
        };

        var forwardA = baseNtt;
        forwardA.Buffer1 = ctx.Workspace.NttBuf1A;
        forwardA.Buffer2 = ctx.Workspace.NttBuf2A;
        forwardA.Twiddle1 = ctx.Ntt.TwiddleFactors.Forward1;
        forwardA.Twiddle2 = ctx.Ntt.TwiddleFactors.Forward2;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardA);

        var forwardB = forwardA;
        forwardB.Buffer1 = ctx.Workspace.NttBuf1B;
        forwardB.Buffer2 = ctx.Workspace.NttBuf2B;
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forwardB);

        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A,
            ctx.Workspace.NttBuf1B, ctx.Workspace.NttBuf2B, m);
        Gpu.SyncGrid();

        var inverseA = baseNtt;
        inverseA.Buffer1 = ctx.Workspace.NttBuf1A;
        inverseA.Buffer2 = ctx.Workspace.NttBuf2A;
        inverseA.Twiddle1 = ctx.Ntt.TwiddleFactors.Inverse1;
        inverseA.Twiddle2 = ctx.Ntt.TwiddleFactors.Inverse2;
        GpuBigIntNttKernels.ExecuteDualInverseNtt(inverseA);

        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(ctx.Workspace.NttBuf1A, ctx.Workspace.NttBuf2A,
            ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh, m);
        Gpu.SyncGrid();

        int chunkSize = BlockDimension.X;
        int numBlocks = (numWordsResult + chunkSize - 1) / chunkSize;

        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(ctx.Workspace.UncarriedLow, ctx.Workspace.UncarriedHigh,
            resultDest, ctx.Workspace.BlockCarries, m, numWordsResult, chunkSize);

        Gpu.SyncGrid();

        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, ctx.Workspace.BlockCarries, numWordsResult,
            chunkSize, numBlocks);

        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    private static void ExecuteBarrettReductionMacro(
        ModPowNttContext ctx,
        uint[] leftOperand, uint[] rightOperand, uint[] reciprocalOfB,
        int targetBits, int aActualWords, uint[] quotientBuffer, uint[] remainderBuffer)
    {
        var nA = aActualWords;
        var n = ctx.Dimensions.NumberOfWords;
        var targetWords = (targetBits + 31) / 32;

        ExecuteNttMultiplyMacroModPow(ctx, leftOperand, reciprocalOfB, ctx.Workspace.CurrentProduct, nA, targetWords,
            nA + targetWords);

        int wordShift = targetBits / 32;
        int bitShift = targetBits % 32;
        int destWords = nA;

        Gpu.GridStrideLoop(destWords, idx =>
        {
            int srcIdx = idx + wordShift;
            uint w0 = (srcIdx < nA + targetWords) ? ctx.Workspace.CurrentProduct[srcIdx] : 0u;
            uint w1 = (srcIdx + 1 < nA + targetWords) ? ctx.Workspace.CurrentProduct[srcIdx + 1] : 0u;
            uint shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
            quotientBuffer[idx] = shiftedValue;
        });
        Gpu.SyncGrid();

        ExecuteNttMultiplyMacroModPow(ctx, quotientBuffer, rightOperand, ctx.Workspace.NextIter, destWords, n,
            destWords + n);

        int evalWords = aActualWords + 1;
        var subCtx = new GpuBigIntContext
            { Operands = new OperandContext { CarryBuffer = ctx.CarryBuffer }, NumberOfWords = evalWords };

        Gpu.GridStrideLoop(evalWords, idx =>
        {
            ctx.Workspace.Diff[idx] = idx < nA ? leftOperand[idx] : 0u;
        });
        Gpu.SyncGrid();

        GpuBigIntKernels.DeviceAddSubParallel(ctx.Workspace.Diff, ctx.Workspace.NextIter, ctx.Workspace.Diff, evalWords,
            -1, true);
        Gpu.SyncGrid();

        for (int iter = 0; iter < 1000; iter++)
        {
            uint topWord = ctx.Workspace.Diff[evalWords - 1];
            bool underflow = (topWord & 0x80000000u) != 0;

            // Wait for all threads to be ready
            Gpu.SyncGrid();

            if (underflow)
            {
                GpuBigIntKernels.DeviceAddSubParallel(ctx.Workspace.Diff, rightOperand, ctx.Workspace.Diff, evalWords,
                    1, true);
                Gpu.SyncGrid();
            }
            else
            {
                // Try subtracting M to see if R >= M
                GpuBigIntKernels.DeviceAddSubParallel(ctx.Workspace.Diff, rightOperand, ctx.Workspace.Diff, evalWords,
                    -1, true);
                Gpu.SyncGrid();

                uint top2 = ctx.Workspace.Diff[evalWords - 1];
                bool underflow2 = (top2 & 0x80000000u) != 0;
                Gpu.SyncGrid();

                if (underflow2)
                {
                    // Was smaller than M, add back and we are done.
                    GpuBigIntKernels.DeviceAddSubParallel(ctx.Workspace.Diff, rightOperand, ctx.Workspace.Diff,
                        evalWords, 1, true);
                    Gpu.SyncGrid();
                    break;
                }
            }
        }

        Gpu.GridStrideLoop(aActualWords, idx =>
        {
            remainderBuffer[idx] = idx < n ? ctx.Workspace.Diff[idx] : 0u;
        });
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    public static void ModPowNttMegaKernel(ModPowNttContext ctx)
    {
        int n = ctx.Dimensions.NumberOfWords;
        int expWords = ctx.ExponentWords;
        int targetBits = ctx.Dimensions.TargetBits;
        int maxABits = 2 * n * 32;
        int aActualWords = (maxABits + 31) / 32;
        int targetWords = (targetBits + 31) / 32;
        int outWords = aActualWords + targetWords;

        // Initialize tempResult = 1, tempBase = Base, ModulusExpanded = Constant
        if (ctx.ExponentStartWord == 0)
        {
            Gpu.GridStrideLoop(outWords, idx =>
            {
                if (idx < 2 * n)
                {
                    ctx.Workspace.AxSquared[idx] = idx == 0 ? 1u : 0u; // TempResult
                    ctx.Workspace.XSquared[idx] = idx < n ? ctx.Operands.TargetOperand[idx] : 0u; // TempBase
                }

                ctx.Workspace.ModulusExpanded[idx] = idx < n ? ctx.Operands.Constant[idx] : 0u;
            });
            Gpu.SyncGrid();
        }

        for (int i = ctx.ExponentStartWord; i < ctx.ExponentEndWord; i++)
        {
            uint e = ctx.Exponent[i];

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
                    ExecuteNttMultiplyMacroModPow(ctx, ctx.Workspace.AxSquared, ctx.Workspace.XSquared,
                        ctx.Workspace.NextXUnshifted, 2 * n, 2 * n, 4 * n);
                    ExecuteBarrettReductionMacro(ctx, ctx.Workspace.NextXUnshifted, ctx.Workspace.ModulusExpanded,
                        ctx.ReciprocalMod, targetBits, aActualWords, ctx.Workspace.NextXShifted,
                        ctx.Workspace.AxSquared);
                }

                ExecuteNttMultiplyMacroModPow(ctx, ctx.Workspace.XSquared, ctx.Workspace.XSquared,
                    ctx.Workspace.NextXUnshifted, 2 * n, 2 * n, 4 * n);
                ExecuteBarrettReductionMacro(ctx, ctx.Workspace.NextXUnshifted, ctx.Workspace.ModulusExpanded,
                    ctx.ReciprocalMod, targetBits, aActualWords, ctx.Workspace.NextXShifted, ctx.Workspace.XSquared);

                e >>= 1;
            }
        }

        // Copy final result
        if (ctx.ExponentEndWord == expWords)
        {
            Gpu.GridStrideLoop(n, idx =>
            {
                ctx.Operands.ResultBuffer[idx] = ctx.Workspace.AxSquared[idx];
            });
            Gpu.SyncGrid();
        }
    }
}
