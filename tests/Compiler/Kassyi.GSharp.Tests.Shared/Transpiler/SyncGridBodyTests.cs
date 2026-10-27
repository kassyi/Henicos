#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Runs the <c>Gpu.SyncGrid(body)</c> kernels of <see cref="SyncGridBodyKernels"/> (#626) with 2 blocks of 32 threads.
/// On the CPU dialects each test runs on the managed path and on the default path (the native library when it exists).
/// The expected values are computed from the inputs, not from the kernel.
/// </summary>
/// <remarks>
/// A kernel that reads what another block wrote before a gap does not run on the native CPU path: the native launcher runs
/// each block to its end on its own, so <c>Gpu.SyncGrid</c> does not wait for the other blocks there (found with these
/// tests; #664). The managed path synchronizes the whole grid.
/// </remarks>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SyncGridBodyTests : TranspilerTestBase
{
    private const int Blocks = 2;
    private const int ThreadsPerBlock = 32;
    private const int Threads = Blocks * ThreadsPerBlock;

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_TwoStatements_BodyRuns(bool forceManaged)
    {
        var a = Filled(-1);

        Run(forceManaged, () => SyncGridBodyKernels.LaunchBodyRunsKernel(a, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(1, Threads), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReadAfterGap_SeesOtherBlocksWrites(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        RunReadingOtherBlocks(forceManaged, () => SyncGridBodyKernels.LaunchNeighborKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_EmptiesExplicitSyncAndNestedBodies_RunEveryStatement(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        RunReadingOtherBlocks(forceManaged, () => SyncGridBodyKernels.LaunchMixedKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 101), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_LocalAcrossGaps_KeepsItsValue(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        RunReadingOtherBlocks(forceManaged, () => SyncGridBodyKernels.LaunchHoistedLocalKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1 + t + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_InsideUniformLoop_SyncsEveryIteration(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        RunReadingOtherBlocks(forceManaged, () => SyncGridBodyKernels.LaunchLoopNestedKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // k = 0 and k = 1: the neighbour's value is (t + 1) % n + 1 + k
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 2 * ((t + 1) % Threads + 1) + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_SiblingBodiesWithSameLocalName_KeepTheirOwnValues(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchSiblingLocalsKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 2 * t), a);
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 4 * t), b);
    }

    private static int[] Filled(int value) => Enumerable.Repeat(value, Threads).ToArray();

    private void RunReadingOtherBlocks(bool forceManaged, Action launch)
    {
        Skip.If(!forceManaged && IsCpuDialect, "native CPU launcher does not wait at grid barriers (#664); the managed run covers the CPU dialect");
        Run(forceManaged, launch);
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
