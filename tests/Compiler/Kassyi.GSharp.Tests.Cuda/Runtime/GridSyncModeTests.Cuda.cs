using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class GridSyncModeTests
{
    // [WHY] Every thread of block 1 fails, so block 1 never arrives and the Fast barrier never completes either: the
    // waiters must leave through the abort word of their launch. The timeout fails the test instead of waiting for a
    // hang that the driver would end only by resetting the GPU.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task FastMode_WholeBlockFails_WaitersLeaveAndLaunchThrows()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[Blocks * BlockSize];
        for (int t = 0; t < BlockSize; t++)
        {
            keys[BlockSize + t] = GridSyncModeKernels.FailKey;
        }

        int[] result = Filled(keys.Length);
        var launch = Task.Run(() => Assert.Throws<SwitchExpressionException>(() =>
            GridSyncModeKernels.LaunchFailBeforeFastGridSyncKernel(keys, result, gridDimX: Blocks,
                blockDimX: BlockSize)));
        var ex = await launch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("(1,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    // [WHY] Only thread 5 of block 0 fails; the other threads of block 0 reach the barrier after it exited. In Fast
    // mode block 0 arrives and then leaves, and its arrival may complete the barrier for blocks 1 to 3, which then
    // write their results: whether they do depends on the order of arrival. The launch throws either way, and no
    // block waits.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task FastMode_OneThreadFails_LaunchThrowsAndNoBlockWaits()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridSyncModeKernels.FailKey;
        int[] result = Filled(keys.Length);

        var launch = Task.Run(() => Assert.Throws<SwitchExpressionException>(() =>
            GridSyncModeKernels.LaunchFailBeforeFastGridSyncKernel(keys, result, gridDimX: Blocks,
                blockDimX: BlockSize)));
        var ex = await launch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
        // Block 0 saw the failure after its arrival and left; a thread of another block wrote its own value or nothing
        Assert.All(result.Take(BlockSize), static value => Assert.Equal(Untouched, value));
        for (int i = BlockSize; i < result.Length; i++)
        {
            Assert.True(result[i] == Untouched || result[i] == i, $"result[{i}] = {result[i]}");
        }
    }
}
