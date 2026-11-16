#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class SharedMemoryLimitTests
{
    // Issue (a): 32 ints need 128 bytes
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DynamicDeclaration_LargerThanSharedMemBytes_ThrowsInvalidOperationException(
        bool forceManaged,
        bool withBarrier)
    {
        var output = new int[32];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run(forceManaged, () => Launch(withBarrier, output, 32, sharedMemBytes: 16)));

        Assert.Contains(LimitMessage, exception.Message);
        Assert.Contains("SharedMemoryLimitKernels.cu.cs(", exception.Message);
    }

    // int.MaxValue ints are 8 GB, which exceed sharedMemBytes before any memory is handed out. The step ends there, and
    // the process goes on: the next launch runs
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DynamicDeclaration_HugeCount_ThrowsAndProcessContinues(bool forceManaged, bool withBarrier)
    {
        var output = new int[32];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run(forceManaged, () => Launch(withBarrier, output, int.MaxValue, sharedMemBytes: 128)));

        Assert.Contains(LimitMessage, exception.Message);
        Run(forceManaged, () => Launch(withBarrier, output, 32, sharedMemBytes: 128));
        Assert.Equal(Enumerable.Range(0, 32).Select(i => withBarrier ? (i + 1) % 32 + 1 : 2 * i), output);
    }

    // Issue (b): 80,000 bytes, past the old 64 KB limit of the native path (which ended the process). CUDA needs the
    // opt-in of more than 48 KB (#545), so this runs on the CPU only
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DynamicDeclaration_LargerThan64Kb_ExecutesSuccessfully(bool forceManaged, bool withBarrier)
    {
        const int Count = 20000;
        var output = new int[Count];

        Run(
            forceManaged,
            () => Launch(withBarrier, output, Count, sharedMemBytes: Count * sizeof(int), blockDimX: 256)
        );

        Assert.Equal(Enumerable.Range(0, Count).Select(i => withBarrier ? (i + 1) % Count + 1 : 2 * i), output);
    }

    // A kernel that is not cooperative runs each block to its end, so the managed path reuses one dynamic shared memory
    // array for the blocks instead of keeping one per block for the grid (PR #762 review, item 3): 1024 blocks of 64 KB
    // would allocate 64 MB, one array 64 KB
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DynamicDeclaration_ManyBlocksManaged_ReusesSingleArray()
    {
        const int Count = 16384;
        const int Blocks = 1024;
        var output = new int[Count];
        Run(
            true,
            () => SharedMemoryLimitKernels.LaunchFillKernel(
                output,
                Count,
                gridDimX: 1,
                blockDimX: 1,
                sharedMemBytes: Count * sizeof(int)
            )
        );

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(
            true,
            () => SharedMemoryLimitKernels.LaunchFillKernel(
                output,
                Count,
                gridDimX: Blocks,
                blockDimX: 1,
                sharedMemBytes: Count * sizeof(int)
            )
        );
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Enumerable.Range(0, Count).Select(static i => (i + 1) % Count + 1), output);
        Assert.True(allocated < 16L << 20, $"the launch allocated {allocated} bytes");
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoDynamicRegions_OneByteShort_ThrowsInvalidOperationException(bool forceManaged)
    {
        var output = new int[8];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run(
                forceManaged,
                () => SharedMemoryLimitKernels.LaunchTwoRegionsKernel(output, 5, 3, blockDimX: 8, sharedMemBytes: 55)
            ));

        Assert.Contains(LimitMessage, exception.Message);
    }

    // A static declaration of 2^32 + 128 bytes: the managed path computed the size in int, got 128 bytes and threw
    // IndexOutOfRangeException at an element inside the declared count. It now fails as the native path does when it
    // cannot allocate the buffer, before allocating anything (#1280 review)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StaticDeclaration_MoreThanIntMaxValueBytesManaged_ThrowsInvalidOperationException()
    {
        var output = new double[1];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(true, () => SharedMemoryHugeStaticKernels.LaunchHugeStaticKernel(output, blockDimX: 1)));

        Assert.Equal("G#: the static shared memory of a declaration could not be allocated", exception.Message);
    }
}
