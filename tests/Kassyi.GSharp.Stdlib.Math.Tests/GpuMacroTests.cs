using System;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.Tests.Kernels;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests;

public class GpuMacroTests
{
    [Theory]
    [Trait("FeatureId", "GEN-13")] // Or another appropriate ID
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(2048)]
    public void Test_MacroKernel_GridStrideLoop_And_SingleThread(int length)
    {
        // Edge cases and stress testing for macro kernels
        using var scope = new JitEngine.CudaContextScope();
        
        var output = new uint[System.Math.Max(1, length)];
        // Fill with random noise to ensure it gets overwritten
        for(int i = 0; i < output.Length; i++) output[i] = 0xDEADBEEF;

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
        
        // For length == 0, output array is length 1 but shouldn't be touched (remains DEADBEEF)
        if (length == 0)
        {
            Assert.Equal(0xDEADBEEF, output[0]);
        }
    }
}
