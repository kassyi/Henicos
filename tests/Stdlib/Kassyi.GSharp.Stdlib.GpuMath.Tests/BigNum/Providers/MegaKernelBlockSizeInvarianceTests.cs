using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Verifies that cooperative mega-kernel results for <c>InverseSqrtMega</c>, <c>Sqrt</c>, and <c>ReciprocalMega</c>
/// are bit-for-bit identical across block sizes <c>1024</c>, <c>768</c>, <c>512</c>, and <c>256</c> (including inputs
/// crossing the 1024-word carry-chunk boundary), honor <c>CandidateBlockSizesOverride</c> with <c>AsyncLocal</c>
/// isolation, and match the CPU oracle.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class MegaKernelBlockSizeInvarianceTests(ITestOutputHelper output)
{
    private const string InverseSqrtKernelName = "GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel";
    private const int BlockSizeOfTheRule = 0;
    private static readonly int[] CandidateBlockSizes = [1024, 768, 512, 256];

    // The product of InverseSqrt has 4 * numWords + 3 * marginWords words and its transform M = the power of two that
    // holds twice as many points. A transform of L-point rows runs rowsPerBatch = max(1, B / (L / 2)) rows at once per
    // block, on G = min(smCount, ceil(M / B)) blocks; smCount 0 keeps the SM count of the machine. Each input is
    // paired with the defect it catches:
    // - (16, 4, 0): M = 256 (2^8, single-row), inequality and override checks in the helper body below.
    // - (1024, 16, 0), (1025, 16, 0): M = 16384 (2^14 = 128 x 128, column-read), carry-chunk boundary checks below.
    // - (50, 8, 0): M = 512, one row; B = 256 gives G = 2 (block 1 has no row).
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:33 `rowsOfBlock` without `+ GridDimension.X - 1` or `- BlockIndex.X`
    // - (60, 6, 0): M = 1024, one row; B = 256 has L / 2 = 512 > B; B = 768 leaves 256 threads idle.
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:60 `id & (halfLength - 1)` -> `id`;
    //           :58 `int slot = id >> (maxLog2Length - 1);` -> `id >> maxLog2Length`
    // - (250, 8, 1): M = 2048 = 32 x 64 (N1 != N2), G = 1; B = 768 has short last batch [48, 16] / [24, 8].
    //   mutant: GpuBigIntNttKernels.cu.cs:377 `args.ColumnStride = args.Grid.N2;` -> `args.Grid.N1`;
    //           GpuBigIntNttKernels.SharedRow.cu.cs:36 `rowsOfBlock - firstSlot < rowsPerBatch ? ... : rowsPerBatch;` -> `rowsPerBatch`
    // - (250, 8, 3): M = 2048 = 32 x 64, B = 256, G = 3 (short last batch differs per block); B = 1024 gives G = 2.
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:37 `int firstRow = BlockIndex.X + firstSlot * GridDimension.X;` -> `+ firstSlot`;
    //           :80 `firstRow + slot * GridDimension.X` -> `firstRow + slot`
    // - (600, 10, 3): M = 8192 = 64 x 128, B = 1024, G = 3 (several batches at the largest block).
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:34 `firstSlot += rowsPerBatch` -> `+= 1`;
    //           :70 `slot * paddedLength` -> `slot * args.CurrentL`
    // - (5000, 2000, 3): M = 65536 = 256 x 256, exact StridedColumnMaxTransformLength boundary (column read), G = 3.
    //   mutant: GpuBigIntNttKernels.cu.cs:376 `args.TemporaryBuffer = args.Buffer;` -> `args.TemporaryBuffer`;
    //           GpuBigIntNttKernels.SharedRow.cu.cs:91 `rowIndex + element * args.ColumnStride;` -> `rowIndex * args.CurrentL + element`
    // - (8000, 4000, 3): M = 131072 = 256 x 512, above StridedColumnMaxTransformLength (transposes, N1 != N2), G = 3.
    //   mutant: GpuBigIntNttKernels.cu.cs:425 / :469 `TransposeTiles(buffer, temporaryBuffer, length2, length1);` -> `(..., length1, length2)`;
    //           :372 `args.Buffer = args.TemporaryBuffer;` -> `args.Buffer`;
    //           GpuBigIntFermatKernels.cu.cs:264 `int tile = BlockIndex.X + (firstSlot + slot) * GridDimension.X;` -> `+ firstSlot + slot`
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
        AssertInverseSqrtMegaMatchesCpuOracle(numWords, marginWords, smCount, CandidateBlockSizes);
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
        AssertInverseSqrtMegaMatchesCpuOracle(numWords, marginWords, smCount, [BlockSizeOfTheRule]);
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

    private static void AssertInverseSqrtMegaMatchesCpuOracle(
        int numWords, int marginWords, int smCount, int[] blockSizes)
    {
        var operandWords = CreateNormalizedWords(seed: 7100 + numWords, numWords);
        var operand = FromWords(operandWords);
        var precisionBits = (numWords + marginWords) * 32;
        var resultWords = numWords + marginWords;

        uint[] RunOnWorkspace(GpuWorkspace workspace)
        {
            using var gpuOperand = new GpuBigUInt(workspace, operandWords);
            using var gpuResult = new GpuBigUInt(workspace, resultWords);
            GpuBigIntDivRem.InverseSqrt(gpuOperand, gpuResult, marginWords);
            return gpuResult.ToHostArray();
        }

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(resultWords * 8, GpuExecutionEngine.GSharpAot);
            cpuWords = RunOnWorkspace(cpuWorkspace);
        }

        // Algebraic defining inequality: floor(2^B / sqrt(a)) satisfies a * x^2 <= 2^(2B) < a * (x + 1)^2.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:83
        //         `ComputeInitialGuess(targetOperand, resultBuffer, targetBits, isSquareRoot: true);` -> removed
        // input: (16, 4)
        var cpuValue = FromWords(cpuWords);
        var twoTo2B = BigInteger.One << (2 * precisionBits);
        Assert.True(operand * cpuValue * cpuValue <= twoTo2B, "CPU inverse sqrt exceeds 2^B / sqrt(a)");
        Assert.True(twoTo2B < operand * (cpuValue + 1) * (cpuValue + 1), "CPU inverse sqrt below floor");

        AssertMegaMatchesCpuOracle(
            resultWords * 8,
            smCount,
            blockSizes,
            cpuWords,
            RunOnWorkspace,
            static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
            4 * numWords + 3 * marginWords);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(4)]
    [InlineData(33)]
    [InlineData(512)]
    [InlineData(2050)]
    public void Sqrt_AcrossCandidateBlockSizes_MatchesCpuOracleBitForBit(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var operandWords = CreateNormalizedWords(seed: 7200 + numWords, numWords);
        var operand = FromWords(operandWords);
        var rootWords = (numWords + 1) / 2;

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(numWords * 6, GpuExecutionEngine.GSharpAot);
            using var cpuOperand = new GpuBigUInt(cpuWorkspace, operandWords);
            using var cpuResult = new GpuBigUInt(cpuWorkspace, numWords);
            GpuBigIntDivRem.Sqrt(cpuOperand, cpuResult);
            cpuWords = cpuResult.ToHostArray();
        }

        // Algebraic defining inequality: floor(sqrt(a)) satisfies r^2 <= a < (r + 1)^2.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:243
        //         `(actualBits + 63) / 64` -> `actualBits / 64`
        // input: 33
        var cpuRoot = FromWords(cpuWords);
        Assert.True(cpuRoot * cpuRoot <= operand, "CPU sqrt exceeds exact root");
        Assert.True(operand < (cpuRoot + 1) * (cpuRoot + 1), "CPU sqrt below floor");

        try
        {
            foreach (var blockSize in CandidateBlockSizes)
            {
                CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [blockSize];
                // Every root of this test goes to the mega-kernel whose block size it checks, at any default threshold
                using var gpuWorkspace = new GpuWorkspace(numWords * 6, GpuExecutionEngine.GSharpAot)
                {
                    SqrtPrecisionDoublingThresholdWords = int.MaxValue,
                };
                using var gpuOperand = new GpuBigUInt(gpuWorkspace, operandWords);
                using var gpuResult = new GpuBigUInt(gpuWorkspace, numWords);
                GpuBigIntDivRem.Sqrt(gpuOperand, gpuResult);
                var gpuWords = gpuResult.ToHostArray();

                // The operand has 32 * numWords bits, so SqrtFloor takes a root of r = ceil(numWords / 2) words and
                // launches the inverse square root of its top r + 1 words with SqrtFloorMarginWords(r) margin words;
                // the active override must match blockSize for that kernel invocation.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:218
                //         `var overrideState = s_candidateBlockSizesOverride.Value;` -> `null`
                // input: 4 with blockSize = 512
                Assert.True(CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                    gpuWorkspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    4 * (rootWords + 1) + 3 * GpuBigIntDivRem.SqrtFloorMarginWords(rootWords),
                    out var chosenBlockSize));
                Assert.Equal(blockSize, chosenBlockSize);

                // Integer sqrt must stay bit-for-bit invariant across candidate block sizes and match CPU oracle.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:473 `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;`
                // input: 512 with blockSize = 768
                Assert.Equal(cpuWords, gpuWords);
            }
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    // - (16, 1024, 0): M = 256 (2^8, single-row), Newton iterations and override checks in the method body below.
    // - (1024, 2 * 1024 * 32 + 512, 0), (1025, 2 * 1025 * 32 + 512, 0): M = 16384 (2^14 = 128 x 128, column-read),
    //   carry-chunk boundary checks in the method body below.
    // - (200, 16000, 3): M = 4096 = 64 x 64, six-step transform of Reciprocal, G = 3; B = 512: [16, 6] / [16, 5].
    //   mutant: GpuBigIntNttKernels.SharedRow.cu.cs:37 `int firstRow = BlockIndex.X + firstSlot * GridDimension.X;` -> `+ firstSlot`;
    //           :80 `firstRow + slot * GridDimension.X` -> `firstRow + slot`
    // - (3000, 2 * 3000 * 32 + 512, 5): M = 32768 = 128 x 256 (N1 != N2, column-read path), G = 5.
    //   mutant: GpuBigIntNttKernels.cu.cs:377 `args.ColumnStride = args.Grid.N2;` -> `args.Grid.N1`
    // - (8000, 2 * 8000 * 32 + 512, 3): M = 131072 = 256 x 512 (N1 != N2, transposes), G = 3.
    //   mutant: GpuBigIntNttKernels.cu.cs:425 / :469 `TransposeTiles(buffer, temporaryBuffer, length2, length1);` -> `(..., length1, length2)`;
    //           GpuBigIntFermatKernels.cu.cs:264 `int tile = BlockIndex.X + (firstSlot + slot) * GridDimension.X;` -> `+ firstSlot + slot`
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16, 1024, 0)]
    [InlineData(1024, 2 * 1024 * 32 + 512, 0)]
    [InlineData(1025, 2 * 1025 * 32 + 512, 0)]
    [InlineData(200, 16000, 3)]
    [InlineData(3000, 2 * 3000 * 32 + 512, 5)]
    [InlineData(8000, 2 * 8000 * 32 + 512, 3)]
    public void ReciprocalMega_AcrossCandidateBlockSizes_MatchesCpuOracleBitForBit(
        int numWords, int targetBits, int smCount)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertReciprocalMegaMatchesCpuOracle(numWords, targetBits, smCount, CandidateBlockSizes);
    }

    // The inputs above at the block size the launcher's rule chooses (no override). (200, 16000, 3): M = 4096 with
    // 3 SMs, so the rule takes 768 threads (4096 <= 2 * 3 * 768).
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(16, 1024, 0)]
    [InlineData(1024, 2 * 1024 * 32 + 512, 0)]
    [InlineData(200, 16000, 3)]
    public void ReciprocalMega_BlockSizeOfTheRule_MatchesCpuOracleBitForBit(int numWords, int targetBits, int smCount)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertReciprocalMegaMatchesCpuOracle(numWords, targetBits, smCount, [BlockSizeOfTheRule]);
    }

    private static void AssertReciprocalMegaMatchesCpuOracle(
        int numWords, int targetBits, int smCount, int[] blockSizes)
    {
        var divisorWords = CreateNormalizedWords(seed: 7300 + numWords, numWords);
        var divisor = FromWords(divisorWords);
        var resultWords = targetBits / 32 + 1;

        uint[] RunOnWorkspace(GpuWorkspace workspace)
        {
            using var gpuDivisor = new GpuBigUInt(workspace, divisorWords);
            using var gpuResult = new GpuBigUInt(workspace, resultWords);
            GpuBigIntDivRem.Reciprocal(gpuDivisor, gpuResult, targetBits);
            return gpuResult.ToHostArray();
        }

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(resultWords * 4, GpuExecutionEngine.GSharpAot);
            cpuWords = RunOnWorkspace(cpuWorkspace);
        }

        // Algebraic defining inequality: floor(2^T / b) satisfies b * x <= 2^T < b * (x + 1).
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:88
        //         `var iterations = CalculateNewtonRaphsonIterationCount(currentBits, targetBits);` -> `1`
        // input: (16, 1024)
        var cpuReciprocal = FromWords(cpuWords);
        var twoToT = BigInteger.One << targetBits;
        Assert.True(divisor * cpuReciprocal <= twoToT, "CPU reciprocal exceeds 2^T / b");
        Assert.True(twoToT < divisor * (cpuReciprocal + 1), "CPU reciprocal below floor");

        AssertMegaMatchesCpuOracle(
            resultWords * 4,
            smCount,
            blockSizes,
            cpuWords,
            RunOnWorkspace,
            static () => GpuBigIntDivRemKernels.ResolveReciprocalNewtonRaphsonMegaKernel(),
            numWords + 2 * ((targetBits + 31) / 32));
    }

    private static void AssertMegaMatchesCpuOracle(
        int workspaceWords,
        int smCount,
        int[] blockSizes,
        uint[] cpuWords,
        Func<GpuWorkspace, uint[]> runOnGpu,
        Func<IntPtr> resolveKernel,
        int productWords)
    {
        try
        {
            foreach (var blockSize in blockSizes)
            {
                CooperativeMegaKernelLauncher.CandidateBlockSizesOverride =
                    blockSize == BlockSizeOfTheRule ? null : [blockSize];
                using var gpuWorkspace = new GpuWorkspace(workspaceWords, GpuExecutionEngine.GSharpAot);
                if (smCount > 0)
                {
                    gpuWorkspace.DeviceSmCount = smCount;
                }

                var gpuWords = runOnGpu(gpuWorkspace);

                if (blockSize != BlockSizeOfTheRule)
                {
                    // Active CandidateBlockSizesOverride must govern the block size chosen by
                    // ResolveCooperativeBlockDim.
                    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                    //         CooperativeMegaKernelLauncher.cs:218
                    //         `var overrideState = s_candidateBlockSizesOverride.Value;` -> `null`
                    // input: InverseSqrt (16, 4) or Reciprocal (16, 1024) with blockSize = 512
                    Assert.True(CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                        gpuWorkspace,
                        resolveKernel,
                        productWords,
                        out var chosenBlockSize));
                    Assert.Equal(blockSize, chosenBlockSize);
                }

                // Kernel carry chunking follows BlockDimension.X, so every candidate block size must agree bit-for-bit
                // with the CPU oracle across single-block and multi-block (>1024 words) inputs.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:473
                //         `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;`
                // input: InverseSqrt (1024, 16) or Reciprocal (1024, 66048) with blockSize = 768
                Assert.Equal(cpuWords, gpuWords);
            }
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    // The launch takes the block size of the rule: with 3 SMs a product of 258 words (M = 1024 <= 2 * 3 * 256) runs
    // 256 threads on min(3, 1024 / 256) = 3 blocks, and one of 2430 words (M = 8192 > 2 * 3 * 1024) the largest block
    // that fits, on 3 blocks.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
    //         CooperativeMegaKernelLauncher.cs:321
    //         `var blockDim = ChooseBlockDim(transformLength, effectiveSmCount, largestFitting, candidates);`
    //         -> `largestFitting`
    // input: 258 words
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(258, 256)]
    [InlineData(2430, 0)]
    public void PrepareLaunchShape_ThreeSms_ChoosesTheBlockSizeOfTheRule(int requiredLen, int expectedBlockSize)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot) { DeviceSmCount = 3 };

        var shape = CooperativeMegaKernelLauncher.PrepareLaunchShape(
            workspace,
            requiredLen,
            static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
            InverseSqrtKernelName);

        // 0: the largest block size that fits, as ResolveCooperativeBlockDim finds it.
        var expected = expectedBlockSize != 0
            ? (uint)expectedBlockSize
            : CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                InverseSqrtKernelName,
                shape.SharedMemBytes);
        Assert.Equal(expected, shape.BlockDim);
        Assert.Equal(3u, shape.GridDim);
    }

    // A single-candidate override must govern the launched block and grid dimensions, not fall back to the default
    // rule (on 258 words with 3 SMs, the default rule falls back to 256 threads on 3 blocks for every override).
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
    //         CooperativeMegaKernelLauncher.cs:317
    //         `var overrideCandidates = CandidateBlockSizesOverride;` -> `null`
    // input: candidateBlockSize = 512 (or 768 / 1024) on 258 words, 3 SMs
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1024, 1u)]
    [InlineData(768, 2u)]
    [InlineData(512, 2u)]
    [InlineData(256, 3u)]
    public void PrepareLaunchShape_ThreeSmsWithCandidateOverride_LaunchesWithTheOverrideBlockSize(
        int candidateBlockSize, uint expectedGridDim)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot) { DeviceSmCount = 3 };

        try
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [candidateBlockSize];
            var shape = CooperativeMegaKernelLauncher.PrepareLaunchShape(
                workspace,
                258,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                InverseSqrtKernelName);

            Assert.Equal((uint)candidateBlockSize, shape.BlockDim);
            Assert.Equal(expectedGridDim, shape.GridDim);
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DefaultBlockSize_AfterMegaKernelLaunches_CachesSupportedBlockSize()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        var words = CreateNormalizedWords(seed: 7400, numWords: 16);
        using var operand = new GpuBigUInt(workspace, words);
        using var invSqrt = new GpuBigUInt(workspace, 20);
        using var reciprocal = new GpuBigUInt(workspace, 33);

        GpuBigIntDivRem.InverseSqrt(operand, invSqrt, marginWords: 4);
        GpuBigIntDivRem.Reciprocal(operand, reciprocal, targetBits: 1024);

        using var scope = new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle);
        var sqrtFunc = GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel();
        var recipFunc = GpuBigIntDivRemKernels.ResolveReciprocalNewtonRaphsonMegaKernel();

        var sqrtSmem = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(4 * 16 + 3 * 4));
        var recipSmem = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(16 + 2 * 32));

        // Default launches without override must populate the default occupancy cache with a valid candidate.
        // mutant: src/Runtime/Kassyi.GSharp.Runtime/Aot/CooperativeLaunchOccupancy.cs:46
        //         `s_blockSizes.TryAdd(key, chosen);` -> removed
        // input: 16-word InverseSqrt + Reciprocal on device 0
        Assert.True(CooperativeMegaKernelLauncher.TryGetCachedDefaultBlockSize(
            sqrtFunc, workspace.DeviceOrdinal, sqrtSmem, out var sqrtBlockSize));
        Assert.True(CooperativeMegaKernelLauncher.TryGetCachedDefaultBlockSize(
            recipFunc, workspace.DeviceOrdinal, recipSmem, out var recipBlockSize));
        output.WriteLine(
            $"Chosen cooperative block sizes on device {workspace.DeviceOrdinal}: " +
            $"InverseSqrt={sqrtBlockSize}, Reciprocal={recipBlockSize}");
        Assert.Contains(sqrtBlockSize, new[] { 1024, 768, 512, 256 });
        Assert.Contains(recipBlockSize, new[] { 1024, 768, 512, 256 });
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public async Task CandidateBlockSizesOverride_ConcurrentAsyncFlows_IsolatesOverrideCachePerAsyncFlow()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        const int RequiredLen = 76;
        var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(RequiredLen));

        try
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [512];
            using var flowACaptured = new ManualResetEventSlim(false);
            using var flowBResolved = new ManualResetEventSlim(false);

            var flowATask = Task.Run(() =>
            {
                flowACaptured.Set();
                flowBResolved.Wait();

                var resolvedA = CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                    workspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    InverseSqrtKernelName,
                    sharedMemBytes);
                var hasCachedA = CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                    workspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    RequiredLen,
                    out var cachedA);
                return (resolvedA, hasCachedA, cachedA);
            });

            flowACaptured.Wait();
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [768];
            var resolvedBFirst = CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                InverseSqrtKernelName,
                sharedMemBytes);
            var hasCachedB = CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                RequiredLen,
                out var cachedB);
            flowBResolved.Set();

            var (resolvedA, hasCachedA, cachedA) = await flowATask;
            var resolvedBSecond = CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                InverseSqrtKernelName,
                sharedMemBytes);

            // Each async flow must keep its own override cache so Flow B ([768]) cannot poison Flow A ([512]), and
            // Flow A cannot clear or overwrite Flow B's cached choice.
            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
            //         CooperativeMegaKernelLauncher.cs:26 `new OverrideState(value)` -> shared static dictionary
            // input: Flow A [512] overlapping Flow B [768]
            Assert.Equal(768u, resolvedBFirst);
            Assert.True(hasCachedB);
            Assert.Equal(768, cachedB);
            Assert.Equal(512u, resolvedA);
            Assert.True(hasCachedA);
            Assert.Equal(512, cachedA);
            Assert.Equal(768u, resolvedBSecond);
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    private static uint[] CreateNormalizedWords(int seed, int numWords)
    {
        var rng = new Random(seed);
        var words = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            words[i] = (uint)rng.Next() | ((uint)rng.Next(0, 2) << 31);
        }

        words[numWords - 1] |= 0x80000000u;
        return words;
    }

    private static BigInteger FromWords(ReadOnlySpan<uint> words) =>
        new(MemoryMarshal.AsBytes(words), isUnsigned: true, isBigEndian: false);
}
