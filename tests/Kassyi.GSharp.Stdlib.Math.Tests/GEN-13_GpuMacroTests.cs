using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.Tests.Kernels;

namespace Kassyi.GSharp.Stdlib.Math.Tests;

[Trait("Category", "GpuExecution")]
public class GEN_13_GpuMacroTests
{
    [Theory]
    [Trait("FeatureId", "GEN-13")] // Or another appropriate ID
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(2048)]
    public void Test_GEN13_MacroKernel_GridStrideLoop_And_SingleThread(int length)
    {
        // Edge cases and stress testing for macro kernels
        using var scope = new JitEngine.CudaContextScope();

        var actualLength = System.Math.Max(0, length);
        var output = new uint[actualLength];
        // Fill with random noise to ensure it gets overwritten
        for (int i = 0; i < output.Length; i++) output[i] = 0xDEADBEEF;

        uint fillValue = 0x55555555;
        uint singleValue = 0x12345678;

        // Determine block and grid size. We want to test GridStrideLoop properly,
        // so we use a fixed block size and enough grids, or even fewer grids to force the loop.
        uint blockSize = 256;
        uint gridSize = (uint)System.Math.Max(1, (length + blockSize - 1) / blockSize);

        // Force loop to execute multiple times per thread for small grid
        if (length > 1024)
        {
            gridSize = 2; // small grid, forces stride loop to jump
        }

        GpuMacroTestKernelsWrapper.Launch_Kernel_MacroTest_FillAndSingle(
            output, length, fillValue, singleValue,
            gridDimX: gridSize, blockDimX: blockSize);

        // Verify the results
        for (int i = 0; i < length; i++)
        {
            if (i == 0)
            {
                Assert.Equal(singleValue, output[i]);
            }
            else
            {
                Assert.Equal(fillValue, output[i]);
            }
        }

        // For actualLength == 0, output array is length 0 and shouldn't be touched.
        // The fact that it didn't throw an AccessViolationException verifies empty array handling.
    }
}
