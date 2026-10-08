using Kassyi.GSharp.Stdlib.GpuMath.Tests.Kernels;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuMacroTests
{
    [SkippableTheory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(2048)]
    public void Test_MacroKernel_GridStrideLoop_And_SingleThread(int length)
    {
        CudaCapability.EnsureGpuAvailable();
        // Edge cases and stress testing for macro kernels
        using var scope = new GpuKernelLoader.CudaContextScope();

        var actualLength = Math.Max(0, length);
        var output = new uint[actualLength];
        // Fill with random noise to ensure it gets overwritten
        Array.Fill(output, 0xDEADBEEF);

        uint fillValue = 0x55555555;
        uint singleValue = 0x12345678;

        // Determine block and grid size. We want to test GridStrideLoop properly,
        // so we use a fixed block size and enough grids, or even fewer grids to force the loop.
        uint blockSize = 256;
        uint gridSize = (uint)Math.Max(1, (length + blockSize - 1) / blockSize);

        // Force loop to execute multiple times per thread for small grid
        if (length > 1024)
        {
            gridSize = 2; // small grid, forces stride loop to jump
        }

        GpuMacroTestKernels.LaunchKernel_MacroTest_FillAndSingle(
            output, length, fillValue, singleValue,
            gridDimX: gridSize, blockDimX: blockSize);

        // Verify the results
        if (length > 0)
        {
            Assert.Equal(singleValue, output[0]);
            Assert.True(output.AsSpan(1).AsStackEnumerable().All(v => v == fillValue));
        }

        // For actualLength == 0, output array is length 0 and shouldn't be touched.
        // The fact that it didn't throw an AccessViolationException verifies empty array handling.
    }
}
