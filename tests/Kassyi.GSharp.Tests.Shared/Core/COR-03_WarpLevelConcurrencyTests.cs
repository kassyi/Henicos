#if GSHARP_CUDA
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Core;

[Trait("Category", "GpuExecution")]
public class COR_03_WarpLevelConcurrencyTests
{
    [SkippableFact]
    [Trait("FeatureId", "COR-03")]
    public void Test_COR03_WarpReduceSum_CalculatesCorrectly()
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "CUDA execution is disabled in this environment.");
        int numWarps = 4;
        int threadsPerWarp = 32;
        int length = numWarps * threadsPerWarp;
        
        var input = new int[length];
        var output = new int[numWarps];

        for (int i = 0; i < length; i++)
        {
            input[i] = 1; // Each thread contributes 1
        }

        // Output array initialized to 0
        COR_03_WarpLevelConcurrencyKernelsWrapper.Launch_WarpReduceSumKernel(
            input,
            output,
            blockDimX: (uint)length
        );

        for (int i = 0; i < numWarps; i++)
        {
            // Sum of 32 threads each contributing 1 should be 32
            Assert.Equal(32, output[i]);
        }
    }

    [SkippableFact]
    [Trait("FeatureId", "COR-03")]
    public void Test_COR03_WarpPrimitives_GenerateAndCompile()
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "CUDA execution is disabled in this environment.");
        int length = 32;
        var input = new int[length];
        var output = new int[length];
        
        COR_03_WarpLevelConcurrencyKernelsWrapper.Launch_WarpPrimitivesKernel(
            input,
            output,
            blockDimX: 32
        );
        
        // This test mostly verifies that all warp intrinsics can be transpiled and compiled by NVCC.
        // It executes successfully without crashing.
    }
}
#endif
