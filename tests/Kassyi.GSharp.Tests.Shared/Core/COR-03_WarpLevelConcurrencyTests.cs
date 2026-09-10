using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Core;

[Trait("Category", "GpuExecution")]
public class COR_03_WarpLevelConcurrencyTests
{
    [Fact]
    [Trait("FeatureId", "COR-03")]
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
}
