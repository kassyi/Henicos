#if GSHARP_CUDA
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public class WarpLevelConcurrencyTests
{
    [SkippableFact]
    public void Test_WarpReduceSum_CalculatesCorrectly()
    {
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
        WarpLevelConcurrencyKernels.LaunchWarpReduceSumKernel(
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
    public void Test_WarpPrimitives_GenerateAndCompile()
    {
        int length = 32;
        var input = new int[length];
        var output = new int[length];
        
        WarpLevelConcurrencyKernels.LaunchWarpPrimitivesKernel(
            input,
            output,
            blockDimX: 32
        );
        
        // This test mostly verifies that all warp intrinsics can be transpiled and compiled by NVCC.
        // It executes successfully without crashing.
    }
}
#endif
