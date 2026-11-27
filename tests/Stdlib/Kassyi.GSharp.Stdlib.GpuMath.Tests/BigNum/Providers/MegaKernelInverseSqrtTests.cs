using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Verifies that cooperative mega-kernel results for <c>InverseSqrtMega</c> are bit-for-bit identical across
/// candidate block sizes <c>1024</c>, <c>768</c>, <c>512</c>, and <c>256</c>, honor the block-size selection rule,
/// refresh kept transforms before the final correction, and match the CPU oracle.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class MegaKernelInverseSqrtTests
{
    // The product of InverseSqrt has 4 * numWords + 3 * marginWords words and its transform M = the power of two that
    // holds twice as many points. A transform of L-point rows runs rowsPerBatch = max(1, B / (L / 2)) rows at once per
    // block, on G = min(smCount, ceil(M / B)) blocks; smCount 0 keeps the SM count of the machine. Each input is
    // paired with the defect it catches:
    // - (16, 4, 0): M = 256 (2^8, single-row), inequality and override checks in the helper body below.
    // - (1024, 16, 0), (1025, 16, 0): M = 16384 (2^14 = 128 x 128, column-read), carry-chunk boundary checks below.
    // - (50, 8, 0): M = 512, one row; B = 256 gives G = 2 (block 1 has no row).
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:33 `rowsOfBlock`
    //           without `+ GridDimension.X - 1` or `- BlockIndex.X`
    // - (60, 6, 0): M = 1024, one row; B = 256 has L / 2 = 512 > B; B = 768 leaves 256 threads idle.
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:60 `id & (halfLength - 1)` -> `id`;
    //           :58 `int slot = id >> (maxLog2Length - 1);` -> `id >> maxLog2Length`
    // - (250, 8, 1): M = 2048 = 32 x 64 (N1 != N2), G = 1; B = 768 has short last batch [48, 16] / [24, 8].
    //   mutant: GpuBigIntNttKernels.cu.cs:377 `args.ColumnStride = args.Grid.N2;` -> `args.Grid.N1`;
    //           GpuBigIntNttKernels.SharedRow.cu.cs:36
    //           `rowsOfBlock - firstSlot < rowsPerBatch ? ... : rowsPerBatch;` -> `rowsPerBatch`
    // - (250, 8, 3): M = 2048 = 32 x 64, B = 256, G = 3 (short last batch differs per block); B = 1024 gives G = 2.
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:37
    //           `int firstRow = BlockIndex.X + firstSlot * GridDimension.X;` -> `+ firstSlot`;
    //           :80 `firstRow + slot * GridDimension.X` -> `firstRow + slot`
    // - (600, 10, 3): M = 8192 = 64 x 128, B = 1024, G = 3 (several batches at the largest block).
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:34 `firstSlot += rowsPerBatch` -> `+= 1`;
    //           :70 `slot * paddedLength` -> `slot * args.CurrentL`
    // - (5000, 2000, 3): M = 65536 = 256 x 256, exact StridedColumnMaxTransformLength boundary (column read), G = 3.
    //   mutant: GpuBigIntNttKernels.cu.cs:376 `args.TemporaryBuffer = args.Buffer;` -> `args.TemporaryBuffer`;
    //           GpuBigIntNttKernels.SharedRow.cu.cs:91
    //           `rowIndex + element * args.ColumnStride;` -> `rowIndex * args.CurrentL + element`
    // - (8000, 4000, 3): M = 131072 = 256 x 512, above StridedColumnMaxTransformLength (transposes, N1 != N2), G = 3.
    //   mutant: GpuBigIntNttKernels.cu.cs:425 / :469
    //           `TransposeTiles(buffer, temporaryBuffer, length2, length1);` -> `(..., length1, length2)`;
    //           :372 `args.Buffer = args.TemporaryBuffer;` -> `args.Buffer`;
    //           GpuBigIntFermatKernels.cu.cs:264
    //           `int tile = BlockIndex.X + (firstSlot + slot) * GridDimension.X;` -> `+ firstSlot + slot`
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16, 4, 0)]
    [InlineData(1024, 16, 0)]
    [InlineData(1025, 16, 0)]
    [InlineData(50, 8, 0)]
    [InlineData(60, 6, 0)]
    [InlineData(250, 8, 1)]
    [InlineData(250, 8, 3)]
    [InlineData(600, 10, 3)]
    [InlineData(5000, 2000, 3)]
    [InlineData(8000, 4000, 3)]
    public void InverseSqrtMega_AcrossCandidateBlockSizes_MatchesCpuOracleBitForBit(
        int numWords, int marginWords, int smCount)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertInverseSqrtMegaMatchesCpuOracle(
            numWords, marginWords, smCount, MegaKernelBlockSizeInvarianceTests.CandidateBlockSizes);
    }

    // The inputs above at the block size the launcher's rule chooses (no override): the smallest candidate c with
    // M <= 2 * SMs * c, else the largest that fits. (50, 8, 0) and (60, 6, 0) run at 256 threads on any GPU of 2 SMs
    // or more, (250, 8, 1) at 1024, (250, 8, 3) at 512, (600, 10, 3) and (8000, 4000, 3) at the largest block that
    // fits.
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(16, 4, 0)]
    [InlineData(1024, 16, 0)]
    [InlineData(50, 8, 0)]
    [InlineData(60, 6, 0)]
    [InlineData(250, 8, 1)]
    [InlineData(250, 8, 3)]
    [InlineData(600, 10, 3)]
    [InlineData(8000, 4000, 3)]
    public void InverseSqrtMega_BlockSizeOfTheRule_MatchesCpuOracleBitForBit(
        int numWords, int marginWords, int smCount)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertInverseSqrtMegaMatchesCpuOracle(
            numWords, marginWords, smCount, [MegaKernelBlockSizeInvarianceTests.BlockSizeOfTheRule]);
    }

    // With iterations = 1 for a 256-bit result (n = 4, margin = 4), the initial guess keeps the top 160 bits (words
    // 3..7) and clears the low 96 bits (words 0..2). The single Newton iteration updates ResultBuffer, so Kept1 (from
    // the start of the iteration) is stale when the final floor correction runs. The correction must refresh
    // Kept1 = T(ResultBuffer) before computing a * x^2 and a * x; otherwise CorrectInverseSqrtSlack sees the stale
    // slack and fails to converge.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntSqrtKernels.cu.cs:97
    //   `GpuBigIntDivRemKernels.ExecuteNttTransformOperand(core, ops.ResultBuffer, xWords, nttScratch.Kept1);`
    //   -> removed
    // input: numWords = 4, marginWords = 4, iterations = 1, bottom 3 words of exact floor cleared
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InverseSqrtMega_FinalCorrectionRefreshesKeptTransform_MatchesDefiningInequality()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 4;
        const int MarginWords = 4;
        const int PrecisionBits = (NumWords + MarginWords) * 32;
        var operandWords = MegaKernelBlockSizeInvarianceTests.CreateNormalizedWords(seed: 2132, NumWords);
        var operand = MegaKernelBlockSizeInvarianceTests.FromWords(operandWords);
        var cpuWords = ComputeInverseSqrtCpuOracle(operandWords, MarginWords, workspaceWords: 256);

        // Keep the top 160 bits of the 256-bit result (words 3..7) and clear the low 96 bits (words 0..2).
        var initialGuessWords = (uint[])cpuWords.Clone();
        initialGuessWords[0] = 0u;
        initialGuessWords[1] = 0u;
        initialGuessWords[2] = 0u;

        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuOperand = new GpuBigUInt(workspace, operandWords);
        using var gpuResult = new GpuBigUInt(workspace, initialGuessWords);

        workspace.Provider.InverseSqrtMega(
            workspace, gpuOperand, gpuResult, MarginWords, actualBits: NumWords * 32, iterations: 1);

        var actualWords = gpuResult.ToHostArray();
        var actualValue = MegaKernelBlockSizeInvarianceTests.FromWords(actualWords);
        var twoTo2B = BigInteger.One << (2 * PrecisionBits);

        Assert.Equal(cpuWords, actualWords);
        Assert.True(operand * actualValue * actualValue <= twoTo2B, "GPU inverse sqrt exceeds 2^B / sqrt(a)");
        Assert.True(twoTo2B < operand * (actualValue + 1) * (actualValue + 1), "GPU inverse sqrt below floor");
    }

    // (30000, 15000, 0): M = 524288 = 512 x 1024. B = 256 transforms the rows of 1024 points with L / 2 > B (each
    // thread takes two butterflies of one row); B = 1024 batches two such rows.
    [SkippableFact]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void InverseSqrtMega_RowsLongerThanTwiceTheBlock_MatchesCpuOracleBitForBit()
    {
        CudaCapability.EnsureGpuAvailable();
        AssertInverseSqrtMegaMatchesCpuOracle(30000, 15000, 0, [1024, 256]);
    }

    private static uint[] ComputeInverseSqrtCpuOracle(uint[] operandWords, int marginWords, int workspaceWords)
    {
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(workspaceWords, GpuExecutionEngine.GSharpAot);
            using var cpuOperand = new GpuBigUInt(cpuWorkspace, operandWords);
            using var cpuResult = new GpuBigUInt(cpuWorkspace, operandWords.Length + marginWords);
            GpuBigIntDivRem.InverseSqrt(cpuOperand, cpuResult, marginWords);
            return cpuResult.ToHostArray();
        }
    }

    private static void AssertInverseSqrtMegaMatchesCpuOracle(
        int numWords, int marginWords, int smCount, int[] blockSizes)
    {
        var operandWords = MegaKernelBlockSizeInvarianceTests.CreateNormalizedWords(seed: 7100 + numWords, numWords);
        var operand = MegaKernelBlockSizeInvarianceTests.FromWords(operandWords);
        var precisionBits = (numWords + marginWords) * 32;
        var resultWords = numWords + marginWords;

        var cpuWords = ComputeInverseSqrtCpuOracle(operandWords, marginWords, resultWords * 8);

        // Algebraic defining inequality: floor(2^B / sqrt(a)) satisfies a * x^2 <= 2^(2B) < a * (x + 1)^2.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:83
        //         `ComputeInitialGuess(targetOperand, resultBuffer, targetBits, isSquareRoot: true);` -> removed
        // input: (16, 4)
        var cpuValue = MegaKernelBlockSizeInvarianceTests.FromWords(cpuWords);
        var twoTo2B = BigInteger.One << (2 * precisionBits);
        Assert.True(operand * cpuValue * cpuValue <= twoTo2B, "CPU inverse sqrt exceeds 2^B / sqrt(a)");
        Assert.True(twoTo2B < operand * (cpuValue + 1) * (cpuValue + 1), "CPU inverse sqrt below floor");

        MegaKernelBlockSizeInvarianceTests.AssertMegaMatchesCpuOracle(
            resultWords * 8,
            smCount,
            blockSizes,
            cpuWords,
            workspace =>
            {
                using var gpuOperand = new GpuBigUInt(workspace, operandWords);
                using var gpuResult = new GpuBigUInt(workspace, resultWords);
                GpuBigIntDivRem.InverseSqrt(gpuOperand, gpuResult, marginWords);
                return gpuResult.ToHostArray();
            },
            static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
            4 * numWords + 3 * marginWords);
    }
}
