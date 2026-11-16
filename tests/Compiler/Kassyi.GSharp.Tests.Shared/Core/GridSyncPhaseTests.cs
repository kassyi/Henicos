#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs the grid syncs of <see cref="GridSyncManagedKernels"/> and <see cref="GridSyncPhaseKernels"/> on CUDA and on both
/// CPU paths, native and managed (#664). The expected values are computed from the inputs, not from the kernel.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class GridSyncPhaseTests : TranspilerTestBase
{
    private const int ThreadsPerBlock = 32;

    // The issue's launch (4 blocks) and more blocks than the OpenMP threads of any test machine, so that one OpenMP thread
    // runs several blocks in each phase
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    public void GlobalBarrier_NeighborThreadRead_MatchesSequentialPhaseValues(bool forceManaged, int blocks)
    {
        var threads = blocks * ThreadsPerBlock;
        var buffer = new int[threads];

        Run(forceManaged, () => GridSyncManagedKernels.LaunchGlobalBarrierKernel(buffer, gridDimX: (uint)blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, threads).Select(t => (t + 1) % threads + 1), buffer);
    }

    // A block keeps its shared memory across its grid syncs, though another block may run on the same OpenMP thread in
    // between
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    public void SharedMemory_AcrossGridSyncs_PreservedByEachBlock(bool forceManaged, int blocks)
    {
        var threads = blocks * ThreadsPerBlock;
        var buffer = new int[threads];
        var output = new int[threads];

        Run(forceManaged, () => GridSyncPhaseKernels.LaunchSharedMemoryAcrossGridSyncKernel(
            buffer, output, ThreadsPerBlock, gridDimX: (uint)blocks, blockDimX: ThreadsPerBlock, sharedMemBytes: ThreadsPerBlock * sizeof(int)));

        Assert.Equal(
            Enumerable.Range(0, threads).Select(t =>
            {
                var block = t / ThreadsPerBlock;
                var local = t % ThreadsPerBlock;
                var next = (t + ThreadsPerBlock) % threads + 1;
                return next + 1000 * block + (local + 1) % ThreadsPerBlock + 2000 * block + (local + 2) % ThreadsPerBlock;
            }),
            output);
    }

    // The threads of a block reach the grid sync after different numbers of steps (a shuffle in warp 0 only): a thread
    // that arrives first waits at the grid sync until every thread of every block has arrived
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void ThreadsReachingGridSync_AfterDifferentSteps_AllWaitForGrid(bool forceManaged, int blocks)
    {
        const int BlockSize = 64;
        var threads = blocks * BlockSize;
        var data = new int[threads];
        var output = new int[threads];

        Run(forceManaged, () => GridSyncPhaseKernels.LaunchShuffleBeforeGridSyncKernel(data, output, gridDimX: (uint)blocks, blockDimX: BlockSize));

        Assert.Equal(Enumerable.Range(0, threads).Select(g => (g + 32) % threads + 1), output);
    }

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
