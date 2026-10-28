#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="BlockBarrierStepKernels"/> on CUDA and on both CPU paths, native and managed. The expected values come
/// from the same kernels written as plain C#, one phase between two barriers at a time over every thread of a block.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class BlockBarrierStepTests : TranspilerTestBase
{
    private const int BlockSize = 64;

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void Test_ShuffleInLaterWarpBeforeBarrier_EveryThreadWaitsAtTheBarrier(bool forceManaged, int blocks)
    {
        var output = new int[blocks * BlockSize];

        Run(forceManaged, () => BlockBarrierStepKernels.LaunchShuffleInLaterWarpKernel(output, gridDimX: (uint)blocks, blockDimX: BlockSize));

        Assert.Equal(ShuffleInLaterWarp(blocks), output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void Test_ShuffleCountsDifferAcrossBarriers_EveryThreadWaitsAtEachBarrier(bool forceManaged, int blocks)
    {
        var output = new int[blocks * BlockSize];

        Run(forceManaged, () => BlockBarrierStepKernels.LaunchShuffleCountsAcrossBarriersKernel(output, gridDimX: (uint)blocks, blockDimX: BlockSize));

        Assert.Equal(ShuffleCountsAcrossBarriers(blocks), output);
    }

    // Warp 1 waits at the block barrier while warp 0 is still in its shuffle, then warp 0 waits at the grid sync while warp
    // 1 is in its shuffle
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void Test_ShufflesAroundBlockBarrierAndGridSync_EveryThreadWaitsAtBoth(bool forceManaged, int blocks)
    {
        var data = new int[blocks * BlockSize];
        var output = new int[blocks * BlockSize];

        Run(forceManaged, () => BlockBarrierStepKernels.LaunchShuffleBarrierAndGridSyncKernel(data, output, gridDimX: (uint)blocks, blockDimX: BlockSize));

        Assert.Equal(ShuffleBarrierAndGridSync(blocks), output);
    }

    // The kernels as plain C#: a shuffle with delta 0 (or a xor with 0) reads the thread's own value
    private static int[] ShuffleInLaterWarp(int blocks)
    {
        var output = new int[blocks * BlockSize];
        for (var block = 0; block < blocks; block++)
        {
            var s = new int[BlockSize];
            for (var tid = 0; tid < BlockSize; tid++)
            {
                s[tid] = tid + 1;
            }

            for (var tid = 0; tid < BlockSize; tid++)
            {
                output[block * BlockSize + tid] = s[(tid + 32) % BlockSize];
            }
        }

        return output;
    }

    private static int[] ShuffleCountsAcrossBarriers(int blocks)
    {
        var output = new int[blocks * BlockSize];
        for (var block = 0; block < blocks; block++)
        {
            var s = new int[BlockSize];
            var other = new int[BlockSize];
            for (var tid = 0; tid < BlockSize; tid++)
            {
                var v = tid;
                for (var i = 0; i <= tid / 32; i++)
                {
                    v += 1;
                }

                s[tid] = v;
            }

            for (var tid = 0; tid < BlockSize; tid++)
            {
                other[tid] = s[(tid + 32) % BlockSize];
            }

            for (var tid = 0; tid < BlockSize; tid++)
            {
                for (var i = 0; i < 2 - tid / 32; i++)
                {
                    other[tid] += 10;
                }

                s[tid] = other[tid];
            }

            for (var tid = 0; tid < BlockSize; tid++)
            {
                output[block * BlockSize + tid] = s[(tid + 32) % BlockSize];
            }
        }

        return output;
    }

    private static int[] ShuffleBarrierAndGridSync(int blocks)
    {
        var total = blocks * BlockSize;
        var data = new int[total];
        for (var block = 0; block < blocks; block++)
        {
            var s = new int[BlockSize];
            for (var tid = 0; tid < BlockSize; tid++)
            {
                s[tid] = block * BlockSize + tid + 1;
            }

            for (var tid = 0; tid < BlockSize; tid++)
            {
                data[block * BlockSize + tid] = s[(tid + 32) % BlockSize];
            }
        }

        var output = new int[total];
        for (var gid = 0; gid < total; gid++)
        {
            output[gid] = data[(gid + BlockSize) % total];
        }

        return output;
    }

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
