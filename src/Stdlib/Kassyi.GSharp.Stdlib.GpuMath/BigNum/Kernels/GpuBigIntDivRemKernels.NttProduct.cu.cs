namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using static NttPrimeArithmetic;

public static partial class GpuBigIntDivRemKernels
{
    // Dummy device function to force the generator to emit NttResidues before methods that take it as a parameter.
    [GpuDeviceFunction]
    private static void ForceEmitNttResiduesForDivRem(NttResidues residues)
    {
    }

    /// <summary>
    /// Unpacks <paramref name="operand"/> into 16-bit limbs and runs the forward dual-prime NTT into
    /// <paramref name="destination"/>. Ends with a grid synchronization.
    /// </summary>
    [GpuDeviceFunction]
    internal static void ExecuteNttTransformOperand(
        NewtonRaphsonContext core,
        [GpuReadOnly] uint[] operand,
        int numWords,
        NttResidues destination)
    {
        var m = core.Ntt.TransformLength;
        var grid = GpuBigIntNttKernels.CreateNttGridContext(m);
        var nttScratch = core.Workspace.NttScratch;
        var dst1 = Gpu.NotNull(destination.Buffer1);
        var dst2 = Gpu.NotNull(destination.Buffer2);

        GpuBigIntFermatKernels.DeviceUnpack16(operand, dst1, dst2, m, numWords);
        Gpu.SyncGrid();

        var forward = new DualNtt
        {
            Buffer1 = dst1,
            Buffer2 = dst2,
            TemporaryBuffer = nttScratch.TmpBuf,
            Twiddle1 = core.Ntt.TwiddleFactors.Forward1,
            Twiddle2 = core.Ntt.TwiddleFactors.Forward2,
            Grid = grid
        };
        GpuBigIntNttKernels.ExecuteDualForwardNtt(forward);
    }

    /// <summary>
    /// Multiplies <paramref name="left"/> and <paramref name="right"/> pointwise in the NTT domain into
    /// <paramref name="destination"/>. <paramref name="destination"/> may alias <paramref name="left"/> or
    /// <paramref name="right"/>, and <paramref name="left"/> may equal <paramref name="right"/> for squaring.
    /// Ends with a grid synchronization.
    /// </summary>
    [GpuDeviceFunction]
    internal static void ExecuteNttPointwiseResidues(
        NewtonRaphsonContext core,
        NttResidues left,
        NttResidues right,
        NttResidues destination)
    {
        var m = core.Ntt.TransformLength;
        var left1 = Gpu.NotNull(left.Buffer1);
        var left2 = Gpu.NotNull(left.Buffer2);
        var right1 = Gpu.NotNull(right.Buffer1);
        var right2 = Gpu.NotNull(right.Buffer2);
        var dst1 = Gpu.NotNull(destination.Buffer1);
        var dst2 = Gpu.NotNull(destination.Buffer2);

        Gpu.GridStrideLoop(m, index =>
        {
            dst1[index] = ModMul1(left1[index], right1[index]);
            dst2[index] = ModMul2(left2[index], right2[index]);
        });
        Gpu.SyncGrid();
    }

    /// <summary>
    /// Runs the inverse dual-prime NTT on <paramref name="residues"/>, recombines via 2-prime CRT, and resolves
    /// carries into <paramref name="resultDest"/>. Ends with a grid synchronization.
    /// </summary>
    [GpuDeviceFunction]
    internal static void ExecuteNttResiduesToWords(
        NewtonRaphsonContext core,
        NttResidues residues,
        uint[] resultDest,
        int numWordsResult,
        int chunkSize)
    {
        var m = core.Ntt.TransformLength;
        var grid = GpuBigIntNttKernels.CreateNttGridContext(m);
        var nttScratch = core.Workspace.NttScratch;
        var res1 = Gpu.NotNull(residues.Buffer1);
        var res2 = Gpu.NotNull(residues.Buffer2);

        var inverse = new DualNtt
        {
            Buffer1 = res1,
            Buffer2 = res2,
            TemporaryBuffer = nttScratch.TmpBuf,
            Twiddle1 = core.Ntt.TwiddleFactors.Inverse1,
            Twiddle2 = core.Ntt.TwiddleFactors.Inverse2,
            Grid = grid
        };
        GpuBigIntNttKernels.ExecuteDualInverseNtt(inverse);

        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(res1, res2,
            nttScratch.UncarriedLow, nttScratch.UncarriedHigh, m);
        Gpu.SyncGrid();

        int numBlocks = (numWordsResult + chunkSize - 1) / chunkSize;
        GpuBigIntNttKernels.ExecuteResolveNttCarriesBlocked(nttScratch.UncarriedLow, nttScratch.UncarriedHigh,
            resultDest, nttScratch.BlockCarries, m, numWordsResult, chunkSize);
        Gpu.SyncGrid();
        GpuBigIntNttKernels.ExecuteResolveNttCarriesApply(resultDest, nttScratch.BlockCarries, numWordsResult,
            chunkSize, numBlocks);
        Gpu.SyncGrid();
    }

    /// <summary>
    /// Multiplies <paramref name="operand"/> by a pre-transformed residue slot <paramref name="kept"/> using
    /// <c>NttScratch.Product</c> as temporary storage and writes <paramref name="numWordsResult"/> words to
    /// <paramref name="resultDest"/>. Ends with a grid synchronization.
    /// </summary>
    [GpuDeviceFunction]
    internal static void ExecuteNttMultiplyByKept(
        NewtonRaphsonContext core,
        [GpuReadOnly] uint[] operand,
        int numWords,
        NttResidues kept,
        uint[] resultDest,
        int numWordsResult,
        int chunkSize)
    {
        var product = core.Workspace.NttScratch.Product;
        ExecuteNttTransformOperand(core, operand, numWords, product);
        ExecuteNttPointwiseResidues(core, product, kept, product);
        ExecuteNttResiduesToWords(core, product, resultDest, numWordsResult, chunkSize);
    }

    /// <summary>
    /// Multiplies two pre-transformed residue slots <paramref name="left"/> and <paramref name="right"/> through
    /// <c>NttScratch.Product</c> and writes <paramref name="numWordsResult"/> words to
    /// <paramref name="resultDest"/>. Ends with a grid synchronization.
    /// </summary>
    [GpuDeviceFunction]
    internal static void ExecuteNttMultiplyKept(
        NewtonRaphsonContext core,
        NttResidues left,
        NttResidues right,
        uint[] resultDest,
        int numWordsResult,
        int chunkSize)
    {
        var product = core.Workspace.NttScratch.Product;
        ExecuteNttPointwiseResidues(core, left, right, product);
        ExecuteNttResiduesToWords(core, product, resultDest, numWordsResult, chunkSize);
    }

    /// <summary>
    /// Copies <paramref name="source"/> shifted right by <paramref name="shiftBits"/> bits into
    /// <paramref name="destination"/>, zero-filling reads past <paramref name="sourceWords"/>. Ends with a grid
    /// synchronization.
    /// </summary>
    [GpuDeviceFunction]
    internal static void ShiftRightIntoResult(
        [GpuReadOnly] uint[] source,
        int sourceWords,
        uint[] destination,
        int destWords,
        int shiftBits)
    {
        int wordShift = shiftBits / 32;
        int bitShift = shiftBits % 32;

        Gpu.GridStrideLoop(destWords, idx =>
        {
            int srcIdx = idx + wordShift;
            uint w0 = (srcIdx < sourceWords) ? source[srcIdx] : 0u;
            uint w1 = (srcIdx + 1 < sourceWords) ? source[srcIdx + 1] : 0u;
            destination[idx] = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
        });
        Gpu.SyncGrid();
    }
}
