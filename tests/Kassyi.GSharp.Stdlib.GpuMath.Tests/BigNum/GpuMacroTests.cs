using Kassyi.GSharp.Stdlib.GpuMath.Tests.Kernels;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuMacroTests
{
    [SkippableTheory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Test_MacroKernel_EmptyOutput_ThrowsArgumentException(int length)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        // Non-positive lengths produce an empty output buffer, which the launcher rejects instead of skipping the launch
        var output = new uint[Math.Max(0, length)];

        var exception = Assert.Throws<ArgumentException>(() =>
            GpuMacroTestKernels.LaunchKernel_MacroTest_FillAndSingle(
                output, length, 0x55555555, 0x12345678, blockDimX: 256));
        Assert.Equal("outputBuffer", exception.ParamName);
    }

    [SkippableTheory]
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
        Assert.Equal(singleValue, output[0]);
        Assert.True(output.AsSpan(1).AsStackEnumerable().All(v => v == fillValue));
    }
}
