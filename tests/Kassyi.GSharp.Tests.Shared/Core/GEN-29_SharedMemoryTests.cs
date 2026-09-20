using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Core;

[Trait("Category", "GpuExecution")]
public class GEN_29_SharedMemoryTests : TranspilerTestBase
{
    [SkippableTheory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(256)]
    [Trait("FeatureId", "GEN-29")]
    public void Test_GEN29_SharedMemory_ReversesArrayCorrectly(int length)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = new int[length];
        var output = new int[length];

        for (int i = 0; i < length; i++)
        {
            input[i] = i;
        }

        // SourceGenerator emits this wrapper class and method.
        GEN_29_SharedMemoryKernelsWrapper.Launch_ReverseArrayWithSharedMemoryKernel(
            input,
            output,
            length,
            blockDimX: 256 // Fixed block size to match the kernel's shared memory allocation
        );

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(length - 1 - i, output[i]);
        }
    }
}
