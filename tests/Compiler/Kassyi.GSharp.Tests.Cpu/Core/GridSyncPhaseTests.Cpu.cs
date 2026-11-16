#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class GridSyncPhaseTests
{
    private const string NeverCompletes = "on the GPU the grid sync never completes";

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockFinishesBeforeGridSync_WhileOthersWait_ThrowsInvalidOperationException(bool forceManaged)
    {
        var a = new int[4 * ThreadsPerBlock];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(
                forceManaged,
                () => GridSyncPhaseKernels.LaunchBlockFinishesBeforeGridSyncKernel(
                    a,
                    gridDimX: 4,
                    blockDimX: ThreadsPerBlock
                )
            ));

        Assert.Contains(
            "G#: block (0,0,0) of GridSyncPhaseKernels.BlockFinishesBeforeGridSyncKernel finished "
                + "while other blocks wait at Gpu.SyncGrid",
            exception.Message
        );
        Assert.Contains(NeverCompletes, exception.Message);
        // No block went past the grid sync
        Assert.DoesNotContain(2, a);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void RepresentativeThreadFinishes_WhileBlockWaitsAtGridSync_ThrowsInvalidOperationException(
        bool forceManaged)
    {
        var a = new int[2 * ThreadsPerBlock];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(
                forceManaged,
                () => GridSyncPhaseKernels.LaunchRepresentativeFinishesBeforeGridSyncKernel(
                    a,
                    gridDimX: 2,
                    blockDimX: ThreadsPerBlock
                )
            ));

        Assert.Contains(
            "G#: thread (0,0,0) of block (1,0,0) of "
                + "GridSyncPhaseKernels.RepresentativeFinishesBeforeGridSyncKernel finished "
                + "while other threads of its block wait at Gpu.SyncGrid",
            exception.Message
        );
        Assert.Contains(NeverCompletes, exception.Message);
        Assert.DoesNotContain(2, a);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void GridSync_InNonCooperativeKernel_ThrowsArgumentException(bool forceManaged, int blocks)
    {
        var a = new int[blocks * ThreadsPerBlock];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(
                forceManaged,
                () => GridSyncPhaseKernels.LaunchNotCooperativeKernel(
                    a,
                    gridDimX: (uint)blocks,
                    blockDimX: ThreadsPerBlock
                )
            ));

        Assert.Contains(
            "G#: Gpu.SyncGrid reached in GridSyncPhaseKernels.NotCooperativeKernel, "
                + "which is not [GpuKernel(Cooperative = true)]",
            exception.Message
        );
        Assert.DoesNotContain(2, a);
    }
}
