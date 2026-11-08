#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The launch dimensions against the limits that every GPU enforces (#799): a launch beyond them throws
/// <see cref="ArgumentOutOfRangeException"/> before any thread runs, on CUDA and on both CPU paths, native and
/// managed, and a launch at them runs every thread. The counting of large grids is covered by
/// <c>LaunchLimitsTests</c>.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class LaunchLimitDifferentialTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, 1u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 0u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 0u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 0u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 0u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 1u, 0u)]
    public void ZeroDimensionLaunch_AutoResultKernel_ThrowsArgumentOutOfRangeException(
        uint gridX,
        uint gridY,
        uint gridZ,
        uint blockX,
        uint blockY,
        uint blockZ)
    {
        int[] src = [1, 2, 3, 4];

        foreach (var forceManaged in Paths())
        {
            Run(forceManaged, () => Assert.Throws<ArgumentOutOfRangeException>(() =>
                LaunchShapeDifferentialKernels.LaunchAutoResultThreeDimensionalKernel(
                    src,
                    salt: 42,
                    gridDimX: gridX,
                    gridDimY: gridY,
                    gridDimZ: gridZ,
                    blockDimX: blockX,
                    blockDimY: blockY,
                    blockDimZ: blockZ)));
        }
    }

    // A void kernel used to return without running anything on the CPU paths
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, 1u, 1u, 1u, 1u, 1u, "gridDimX", "is 0")]
    [InlineData(1u, 0u, 1u, 1u, 1u, 1u, "gridDimY", "is 0")]
    [InlineData(1u, 1u, 0u, 1u, 1u, 1u, "gridDimZ", "is 0")]
    [InlineData(1u, 1u, 1u, 0u, 1u, 1u, "blockDimX", "is 0")]
    [InlineData(1u, 1u, 1u, 1u, 0u, 1u, "blockDimY", "is 0")]
    [InlineData(1u, 1u, 1u, 1u, 1u, 0u, "blockDimZ", "is 0")]
    [InlineData(1u, 65536u, 1u, 1u, 1u, 1u, "gridDimY", "at most 65535 blocks in y")]
    [InlineData(1u, 1u, 65536u, 1u, 1u, 1u, "gridDimZ", "at most 65535 blocks in z")]
    [InlineData(1u, 1u, 1u, 1025u, 1u, 1u, "blockDimX", "at most 1024 threads in x")]
    [InlineData(1u, 1u, 1u, 1u, 1025u, 1u, "blockDimY", "at most 1024 threads in y")]
    [InlineData(1u, 1u, 1u, 16u, 1u, 65u, "blockDimZ", "at most 64 threads in z")]
    [InlineData(1u, 1u, 1u, 32u, 33u, 1u, "blockDimX", "exceeds the 1024 threads per block")]
    [InlineData(1u, 1u, 1u, 2048u, 1u, 1u, "blockDimX", "at most 1024 threads in x")]
    [InlineData(1u, 70000u, 1u, 1u, 1u, 1u, "gridDimY", "at most 65535 blocks in y")]
    public void LaunchBeyondTheLimitsOfEveryGpu_ThrowsBeforeAnyThreadRuns(
        uint gridX,
        uint gridY,
        uint gridZ,
        uint blockX,
        uint blockY,
        uint blockZ,
        string paramName,
        string limit)
    {
        foreach (var forceManaged in Paths())
        {
            var output = new int[16];

            var exception = Run(forceManaged, () => Assert.Throws<ArgumentOutOfRangeException>(() =>
                LaunchLimitDifferentialKernels.LaunchWriteLinearRankKernel(
                    output,
                    gridDimX: gridX,
                    gridDimY: gridY,
                    gridDimZ: gridZ,
                    blockDimX: blockX,
                    blockDimY: blockY,
                    blockDimZ: blockZ)));

            Assert.Equal(paramName, exception.ParamName);
            Assert.Contains(limit, exception.Message);
            Assert.Contains("LaunchLimitDifferentialKernels.WriteLinearRankKernel", exception.Message);
            Assert.All(output, static value => Assert.Equal(0, value));
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1u, 1u, 1u, 1024u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 1024u, 1u)]
    [InlineData(1u, 1u, 1u, 16u, 1u, 64u)]
    [InlineData(1u, 1u, 1u, 32u, 32u, 1u)]
    [InlineData(1u, 65535u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 65535u, 1u, 1u, 1u)]
    [InlineData(3u, 5u, 7u, 4u, 2u, 2u)]
    public void LaunchAtTheLimitsOfEveryGpu_RunsEveryThread(
        uint gridX,
        uint gridY,
        uint gridZ,
        uint blockX,
        uint blockY,
        uint blockZ)
    {
        var threads = (int)(gridX * gridY * gridZ * blockX * blockY * blockZ);
        foreach (var forceManaged in Paths())
        {
            var output = new int[threads];

            Run(forceManaged, () => LaunchLimitDifferentialKernels.LaunchWriteLinearRankKernel(
                output,
                gridDimX: gridX,
                gridDimY: gridY,
                gridDimZ: gridZ,
                blockDimX: blockX,
                blockDimY: blockY,
                blockDimZ: blockZ));

            for (var i = 0; i < threads; i++)
            {
                Assert.Equal(i + 1, output[i]);
            }
        }
    }

    // The native CPU path (or CUDA) first, then the managed path on the CPU dialect
    private bool[] Paths() => IsCudaDialect ? [false] : [false, true];

    private static void Run(bool forceManaged, Action launch) => Run(forceManaged, () =>
    {
        launch();
        return 0;
    });

    private static T Run<T>(bool forceManaged, Func<T> launch)
    {
        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            return launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
