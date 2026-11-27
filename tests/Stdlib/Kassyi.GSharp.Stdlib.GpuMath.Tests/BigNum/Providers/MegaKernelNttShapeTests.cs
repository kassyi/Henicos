using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

// Properties checked here (property-test-design §1), for the transform shape of the cooperative mega-kernels:
// - The dynamic shared memory the launcher passes covers what ExecuteNttSharedRow asks for, at every block size the
//   launcher can choose and every row length of every transform length.
// - The two length limits of the transform stay at their measured values.
// Every expected value is computed here from the formulas of the kernel, not read from the launcher.
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class MegaKernelNttShapeTests
{
    private static readonly int[] s_blockSizes = [1024, 768, 512, 256];

    // ExecuteNttSharedRow holds max(1, B >> (log2 L - 1)) rows of L + L / 32 words, for the transform as one row up to
    // 1024 points and for the rows of length N1 and N2 (N2 the smallest power of two with N2 * N2 >= M) above.
    // mutant: CooperativeMegaKernelLauncher.cs `Math.Max(2 * MaxThreadsPerBlock, maxL)` -> `maxL`
    // input: M = 2048, B = 1024 (rows of 32 points, 64 at once: 8448 bytes against 520)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ComputeMegaKernelSharedMemoryBytes_EveryBlockSizeAndRowLength_CoversTheKernelRequest()
    {
        for (var log2M = 1; log2M <= 25; log2M++)
        {
            var transformLength = 1 << log2M;
            var hostBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(transformLength);
            var rowLengths = RowLengthsFor(transformLength);

            foreach (var blockSize in s_blockSizes)
            {
                foreach (var rowLength in rowLengths)
                {
                    var kernelBytes = KernelRowBytes(blockSize, rowLength);
                    Assert.True(kernelBytes <= hostBytes,
                        $"M = {transformLength}, B = {blockSize}, L = {rowLength}: the kernel asks for {kernelBytes} " +
                        $"bytes, the launcher passes {hostBytes}");
                }
            }
        }
    }

    // The sizes themselves: 2 * 1024 points of rows up to 2^22 points, then one row of N2 = 4096 and 8192 points.
    // mutant: CooperativeMegaKernelLauncher.cs:169 `(rowWords + (rowWords >> 5)) * 4 + 256` -> `... * 4`
    // input: any pinned transform length (512, 1024, 1 << 24, 1 << 25)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(512, 8704u)]
    [InlineData(1024, 8704u)]
    [InlineData(1 << 24, 17152u)]
    [InlineData(1 << 25, 34048u)]
    public void ComputeMegaKernelSharedMemoryBytes_PinnedTransformLengths_MatchExpectedBytes(
        int transformLength, uint expectedBytes)
    {
        Assert.Equal(expectedBytes, CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(transformLength));
    }

    private static int[] RowLengthsFor(int transformLength)
    {
        if (transformLength <= 1024)
        {
            return [transformLength];
        }

        var n2 = 1;
        while ((long)n2 * n2 < transformLength)
        {
            n2 *= 2;
        }

        return [transformLength / n2, n2];
    }

    private static long KernelRowBytes(int blockSize, int rowLength)
    {
        var log2L = 0;
        while ((1 << log2L) < rowLength)
        {
            log2L++;
        }

        var rowsPerBatch = Math.Max(1, blockSize >> (log2L - 1));
        return (long)rowsPerBatch * (rowLength + (rowLength >> 5)) * 4;
    }

    /// <summary>
    /// The block-size rule by hand: the smallest candidate c, at most the largest that fits, with M &lt;= 2 * SMs * c,
    /// else the largest that fits.
    /// </summary>
    public static TheoryData<int, int, uint, int[], uint> BlockSizeRuleCases() => new()
    {
        // 48 SMs: 256 up to 24576 points (2 * 48 * 256, included), 512 up to 49152, 768 up to 73728, then 1024.
        { 512, 48, 1024, [1024, 768, 512, 256], 256 },
        { 16384, 48, 1024, [1024, 768, 512, 256], 256 },
        { 24576, 48, 1024, [1024, 768, 512, 256], 256 },
        { 32768, 48, 1024, [1024, 768, 512, 256], 512 },
        { 65536, 48, 1024, [1024, 768, 512, 256], 768 },
        { 131072, 48, 1024, [1024, 768, 512, 256], 1024 },
        // The largest that fits caps the choice, and is the fallback.
        { 131072, 48, 768, [1024, 768, 512, 256], 768 },
        // 1 SM: 256 up to 512 points, 512 up to 1024, then the largest.
        { 512, 1, 1024, [1024, 768, 512, 256], 256 },
        { 1024, 1, 1024, [1024, 768, 512, 256], 512 },
        { 4096, 1, 1024, [1024, 768, 512, 256], 1024 },
        // One candidate (a test override): it is also the largest that fits.
        { 512, 48, 768, [768], 768 },
        // Ascending candidates: must keep the smallest matching candidate (256), not overwrite upward to 1024.
        { 512, 48, 1024, [256, 512, 768, 1024], 256 },
        // largestFitting caps the choice even when a larger candidate satisfies M <= 2 * SMs * c.
        { 2048, 1, 768, [1024, 768, 512, 256], 768 },
    };

    // Every block keeps its rows in one batch while M <= 2 * SMs * c: below that a larger block only waits longer at
    // each barrier, above it a larger block runs fewer batches.
    // mutant: CooperativeMegaKernelLauncher.cs:322 `transformLength <= 2L * ...` -> `<` (input 24576)
    // mutant: CooperativeMegaKernelLauncher.cs:322 `2L * deviceSmCount` -> `1L * deviceSmCount` (input 16384)
    // mutant: CooperativeMegaKernelLauncher.cs:322 `candidate < chosen` -> `candidate > chosen` (input 512, 48)
    // mutant: CooperativeMegaKernelLauncher.cs:322 `candidate < chosen &&` -> removed
    //         (inputs 512 with [256, 512, 768, 1024]; 2048, 1, 768)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(BlockSizeRuleCases))]
    public void ChooseBlockDim_TransformAndSmCount_IsTheSmallestBlockThatKeepsOneBatch(
        int transformLength, int smCount, uint largestFitting, int[] candidates, uint expected)
    {
        Assert.Equal(expected,
            CooperativeMegaKernelLauncher.ChooseBlockDim(transformLength, smCount, largestFitting, candidates));
    }

    // A transform up to 1024 points runs as one shared-memory row: above it the six-step split is faster at every
    // block size (measured), at 1024 the single row is faster with blocks of 512 threads or more. No result depends on
    // the value, so only this test notices a change of it.
    // mutant: GpuBigIntNttKernels.cu.cs `SharedRowMaxTransformLength = 1024` -> `512`
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedRowMaxTransformLength_Is1024()
    {
        Assert.Equal(1024, GpuBigIntNttKernels.SharedRowMaxTransformLength);
    }

    // A transform up to 2^16 points reads the columns of its matrix in place (columns 256 words apart); a longer one
    // transposes the matrix first, which was measured faster from 2^17 points at every block size of 512 threads or
    // more. No result depends on the value, so only this test notices a change of it.
    // mutant: GpuBigIntNttKernels.cu.cs `StridedColumnMaxTransformLength = 1 << 16` -> `1 << 17`
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StridedColumnMaxTransformLength_Is65536()
    {
        Assert.Equal(65536, GpuBigIntNttKernels.StridedColumnMaxTransformLength);
    }
}
