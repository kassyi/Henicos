using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Trait("Category", "GpuExecution")]
public class SharedMemoryTests : TranspilerTestBase
{
    [SkippableTheory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(256)]
    public void Test_SharedMemory_ReversesArrayCorrectly(int length)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = new int[length];
        var output = new int[length];

        for (int i = 0; i < length; i++)
        {
            input[i] = i;
        }

        // SourceGenerator emits this wrapper class and method.
        SharedMemoryKernels.LaunchReverseArrayWithSharedMemoryKernel(
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
