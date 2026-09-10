using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Core;

[Trait("Category", "GpuExecution")]
public class COR_02_SharedMemoryTests
{
    [Fact]
    [Trait("FeatureId", "COR-02")]
    public void Test_SharedMemory_ReversesArrayCorrectly()
    {
        int length = 256;
        var input = new int[length];
        var output = new int[length];

        for (int i = 0; i < length; i++)
        {
            input[i] = i;
        }

        // SourceGenerator emits this wrapper class and method.
        COR_02_SharedMemoryKernelsWrapper.Launch_ReverseArrayWithSharedMemoryKernel(
            input,
            output,
            length,
            blockDimX: (uint)length
        );

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(length - 1 - i, output[i]);
        }
    }
}
