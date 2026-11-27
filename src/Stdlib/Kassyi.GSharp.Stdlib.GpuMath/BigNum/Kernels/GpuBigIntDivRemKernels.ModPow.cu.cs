
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntDivRemKernels
{
    /// <summary>
    /// Returns the low word of <c>1 mod m</c> for an <paramref name="numWords"/>-word modulus <c>m &gt; 0</c>:
    /// 0 when <c>m</c> is 1, otherwise 1 (and every higher word of <c>1 mod m</c> is 0).
    /// </summary>
    /// <remarks>
    /// ModPow starts its result at <c>1 mod m</c>, not at 1: with exponent 0 the result is never reduced, so starting
    /// at 1 would return 1 for modulus 1. Computed on the device so that neither ModPow path reads the modulus back.
    /// </remarks>
    [GpuDeviceFunction]
    private static uint OneModulo(uint[] modulus, int numWords)
    {
        uint highWords = 0u;
        for (int i = 1; i < numWords; i++)
        {
            highWords |= modulus[i];
        }

        return modulus[0] == 1u && highWords == 0u ? 0u : 1u;
    }

    /// <summary>
    /// Writes the low word of <c>1 mod m</c> into <paramref name="result"/>[0]; the caller clears the other words.
    /// Launch with one block of one thread.
    /// </summary>
    [GpuKernel]
    public static void InitializeOneModuloKernel(uint[] result, uint[] modulus, int numWords)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = OneModulo(modulus, numWords);
        }
    }

    [GpuDeviceFunction]
    private static void ExecuteBarrettReductionMacro(
        NewtonRaphsonContext core,
        uint[] leftOperand, uint[] rightOperand,
        int targetBits, int aActualWords, uint[] quotientBuffer, uint[] remainderBuffer,
        int chunkSize)
    {
        var nA = aActualWords;
        var n = core.Dimensions.NumberOfWords;
        var targetWords = (targetBits + 31) / 32;
        var iterScratch = core.Workspace.Iteration;
        var nttScratch = core.Workspace.NttScratch;
        uint[] currentProduct = Gpu.NotNull(iterScratch.CurrentProduct);
        uint[] nextIter = Gpu.NotNull(iterScratch.NextIter);

        // Kept2 holds R̂ = T(ReciprocalMod); Kept3 holds Q̂ = T(modulusExpanded).
        ExecuteNttTransformOperand(core, leftOperand, nA, nttScratch.Product);
        ExecuteNttPointwiseResidues(core, nttScratch.Product, nttScratch.Kept2, nttScratch.Product);
        ExecuteNttResiduesToWords(core, nttScratch.Product, currentProduct, nA + targetWords, chunkSize);

        int wordShift = targetBits / 32;
        int bitShift = targetBits % 32;
        int destWords = nA;

        Gpu.GridStrideLoop(destWords, idx =>
        {
            int srcIdx = idx + wordShift;
            uint w0 = (srcIdx < nA + targetWords) ? currentProduct[srcIdx] : 0u;
            uint w1 = (srcIdx + 1 < nA + targetWords) ? currentProduct[srcIdx + 1] : 0u;
            uint shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
            quotientBuffer[idx] = shiftedValue;
        });
        Gpu.SyncGrid();

        ExecuteNttTransformOperand(core, quotientBuffer, destWords, nttScratch.Product);
        ExecuteNttPointwiseResidues(core, nttScratch.Product, nttScratch.Kept3, nttScratch.Product);
        ExecuteNttResiduesToWords(core, nttScratch.Product, nextIter, destWords + n, chunkSize);

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

        GpuBigIntKernels.DeviceAddSubParallel(iterScratch.Diff, nextIter, iterScratch.Diff, evalWords,
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
        var nttScratch = core.Workspace.NttScratch;
        uint[] axSquared = Gpu.NotNull(iterScratch.AxSquared);
        uint[] xSquared = Gpu.NotNull(iterScratch.XSquared);
        uint[] nextXUnshifted = Gpu.NotNull(iterScratch.NextXUnshifted);
        uint[] nextXShifted = Gpu.NotNull(iterScratch.NextXShifted);
        uint[] modulusExpanded = Gpu.NotNull(iterScratch.ModulusExpanded);
        var expSlice = ctx.Exponent;
        int n = core.Dimensions.NumberOfWords;
        int expWords = expSlice.TotalWords;
        int targetBits = core.Dimensions.TargetBits;
        int maxABits = 2 * n * 32;
        int aActualWords = (maxABits + 31) / 32;
        int targetWords = (targetBits + 31) / 32;
        int outWords = aActualWords + targetWords;
        int chunkSize = BlockDimension.X;

        // Initialize tempResult = 1 mod Constant, tempBase = Base, ModulusExpanded = Constant,
        // and pre-transform Kept2 = R̂ = T(ReciprocalMod), Kept3 = Q̂ = T(modulusExpanded) once across chunk launches.
        if (expSlice.StartWord == 0)
        {
            uint[] constant = Gpu.NotNull(core.Operands.Constant);
            uint[] target = Gpu.NotNull(core.Operands.TargetOperand);
            uint oneModulo = OneModulo(constant, n);
            Gpu.GridStrideLoop(outWords, idx =>
            {
                if (idx < 2 * n)
                {
                    axSquared[idx] = idx == 0 ? oneModulo : 0u; // TempResult
                    xSquared[idx] = idx < n ? target[idx] : 0u; // TempBase
                }

                modulusExpanded[idx] = idx < n ? constant[idx] : 0u;
            });
            Gpu.SyncGrid();

            ExecuteNttTransformOperand(core, ctx.ReciprocalMod, targetWords, nttScratch.Kept2);
            ExecuteNttTransformOperand(core, modulusExpanded, n, nttScratch.Kept3);
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
                // Kept1 holds x̂Sq = T(xSquared), reused by both the conditional multiply and the squaring.
                ExecuteNttTransformOperand(core, xSquared, 2 * n, nttScratch.Kept1);

                if ((e & 1) == 1)
                {
                    ExecuteNttTransformOperand(core, axSquared, 2 * n, nttScratch.Product);
                    ExecuteNttPointwiseResidues(core, nttScratch.Product, nttScratch.Kept1, nttScratch.Product);
                    ExecuteNttResiduesToWords(core, nttScratch.Product, nextXUnshifted, 4 * n, chunkSize);
                    ExecuteBarrettReductionMacro(core, nextXUnshifted, modulusExpanded,
                        targetBits, aActualWords, nextXShifted, axSquared, chunkSize);
                }

                ExecuteNttPointwiseResidues(core, nttScratch.Kept1, nttScratch.Kept1, nttScratch.Product);
                ExecuteNttResiduesToWords(core, nttScratch.Product, nextXUnshifted, 4 * n, chunkSize);
                ExecuteBarrettReductionMacro(core, nextXUnshifted, modulusExpanded,
                    targetBits, aActualWords, nextXShifted, xSquared, chunkSize);

                e >>= 1;
            }
        }

        // Copy final result
        if (expSlice.EndWord == expWords)
        {
            Gpu.GridStrideLoop(n, idx =>
            {
                core.Operands.ResultBuffer[idx] = axSquared[idx];
            });
            Gpu.SyncGrid();
        }
    }
}
