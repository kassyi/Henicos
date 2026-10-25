using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
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

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SharedMemory_RefIndexer_AtomicAddIncrementsAcrossThreads()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[1];

        SharedMemoryRefIndexerKernels.LaunchAtomicAddSharedMemoryKernel(output, blockDimX: 32);

        Assert.Equal(32, output[0]);
    }

#if GSHARP_CPU
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_SharedMemory_MBarrier_CpuDialectThrowsNotSupportedException()
    {
        var output = new int[32];

        Assert.Throws<NotSupportedException>(() =>
            SharedMemoryRefIndexerKernels.LaunchMBarrierSharedMemoryKernel(output, blockDimX: 32));
    }
#else
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SharedMemory_MBarrier_SynchronizesSharedMemoryWrites()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[32];

        SharedMemoryRefIndexerKernels.LaunchMBarrierSharedMemoryKernel(output, blockDimX: 32);

        for (int tid = 0; tid < 32; tid++)
        {
            Assert.Equal(3 * ((tid + 1) % 32), output[tid]);
        }
    }
#endif
}
