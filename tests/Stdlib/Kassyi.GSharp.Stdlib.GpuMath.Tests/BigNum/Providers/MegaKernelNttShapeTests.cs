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
            int[] rowLengths;
            if (transformLength <= 1024)
            {
                rowLengths = [transformLength];
            }
            else
            {
                var n2 = 1;
                while ((long)n2 * n2 < transformLength)
                {
                    n2 *= 2;
                }

                rowLengths = [transformLength / n2, n2];
            }

            foreach (var blockSize in s_blockSizes)
            {
                foreach (var rowLength in rowLengths)
                {
                    var log2L = 0;
                    while ((1 << log2L) < rowLength)
                    {
                        log2L++;
                    }

                    var rowsPerBatch = Math.Max(1, blockSize >> (log2L - 1));
                    long kernelBytes = (long)rowsPerBatch * (rowLength + (rowLength >> 5)) * 4;
                    Assert.True(kernelBytes <= hostBytes,
                        $"M = {transformLength}, B = {blockSize}, L = {rowLength}: the kernel asks for {kernelBytes} " +
                        $"bytes, the launcher passes {hostBytes}");
                }
            }
        }

        // The sizes themselves: 2 * 1024 points of rows up to 2^22 points, then one row of N2 = 4096 and 8192 points.
        // mutant: CooperativeMegaKernelLauncher.cs `(rowWords + (rowWords >> 5)) * 4 + 256` -> `... * 4`
        Assert.Equal(8704u, CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(512));
        Assert.Equal(8704u, CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(1024));
        Assert.Equal(17152u, CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(1 << 24));
        Assert.Equal(34048u, CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(1 << 25));
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
