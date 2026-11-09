using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Every launch records a device error under its own epoch: its launcher throws only its own error, an error that
/// nothing consumed is thrown by the launch that reuses its record, the waiters at the grid barrier leave on the abort
/// word of their launch even when a host synchronization consumed the record, and a launch captured into a CUDA graph
/// still reports its error at the synchronization after the graph launch.
/// </summary>
/// <remarks>
/// [WHY] Sequential: some of these tests leave a record for a synchronization that consumes the record of any launch, or
/// count launches, which a test running beside them would disturb.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class LaunchEpochTests
{
    private const int Untouched = -7;
    private const int BlockSize = 32;

    // The number of per-launch records (DeviceErrorSlot.LaunchRecords): a launch reuses the record of the launch this
    // many epochs before it
    private const int LaunchRecords = 256;

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TwoStreams_OnlyTheFailingLaunchThrows()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var failingStream = new CudaStream();
        using var healthyStream = new CudaStream();

        // The failing launch goes through the Ptr launcher, which leaves its record to the next synchronization of its
        // stream; the driver alone waits for it, so the record is still there when the launch of the same kernel on the
        // other stream reads the records. A launcher that took any record would throw the failing launch's error.
        var failingKeys = new int[BlockSize];
        failingKeys[5] = LaunchEpochKernels.FailKey;
        using var dKeys = new GpuBuffer<int>(BlockSize);
        using var dResult = new GpuBuffer<int>(BlockSize);
        dKeys.CopyToDevice(failingKeys);
        LaunchEpochKernels.LaunchDelayedWriteKernelPtr(dKeys.ToProxy(), 0, dResult.ToProxy(), blockDimX: BlockSize,
            hStream: failingStream.Handle);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(failingStream.Handle));

        var healthyResult = Filled(BlockSize);
        LaunchEpochKernels.LaunchDelayedWriteKernel(new int[BlockSize], 0, healthyResult, blockDimX: BlockSize,
            hStream: healthyStream.Handle);
        Assert.Equal(Enumerable.Range(0, BlockSize), healthyResult);

        // The synchronization of the failing stream throws the failing launch's error
        var ex = Assert.Throws<SwitchExpressionException>(failingStream.Synchronize);
        Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Null(ex.InnerException);
        var failingResult = new int[BlockSize];
        dResult.CopyToHost(failingResult);
        Assert.Equal(6, failingResult[6]);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_FailingLaunch_LauncherConsumesItsRecord()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[BlockSize];
        keys[3] = LaunchEpochKernels.FailKey;

        Assert.Throws<SwitchExpressionException>(() =>
            LaunchEpochKernels.LaunchDelayedWriteKernel(keys, 0, Filled(BlockSize), blockDimX: BlockSize));

        Assert.False(DeviceErrorSlot.TryConsumeSlot(out _), "the launcher left its record for another synchronization");
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnconsumedRecord_IsThrownByTheLaunchThatReusesIt()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();

        // A failing launch through the Ptr launcher, which leaves its record to a later synchronization, and a stream
        // synchronization through the driver alone, which consumes nothing
        var keys = new int[BlockSize];
        keys[9] = LaunchEpochKernels.FailKey;
        using (var dKeys = new GpuBuffer<int>(BlockSize))
        using (var dResult = new GpuBuffer<int>(BlockSize))
        {
            dKeys.CopyToDevice(keys);
            LaunchEpochKernels.LaunchDelayedWriteKernelPtr(dKeys.ToProxy(), 0, dResult.ToProxy(), blockDimX: BlockSize,
                hStream: stream.Handle);
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream.Handle));
        }

        // The next LaunchRecords - 1 launches own other records; the one after reuses the failing launch's record and
        // throws its error before it launches
        var healthyKeys = new int[BlockSize];
        for (int launch = 1; launch < LaunchRecords; launch++)
        {
            LaunchEpochKernels.LaunchDelayedWriteKernel(healthyKeys, 0, Filled(BlockSize), blockDimX: BlockSize);
        }

        var notLaunched = Filled(BlockSize);
        var ex = Assert.Throws<SwitchExpressionException>(() =>
            LaunchEpochKernels.LaunchDelayedWriteKernel(healthyKeys, 0, notLaunched, blockDimX: BlockSize));
        Assert.Equal("(9,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.All(notLaunched, static value => Assert.Equal(Untouched, value));

        // The record is consumed: the launch after it runs
        var next = Filled(BlockSize);
        LaunchEpochKernels.LaunchDelayedWriteKernel(healthyKeys, 0, next, blockDimX: BlockSize);
        Assert.Equal(Enumerable.Range(0, BlockSize), next);
    }

    // [WHY] A synchronization of another stream consumes the record of any launch. The waiters of the launch read its
    // abort word, which no host synchronization clears, so they leave even though the record is gone; a waiter that
    // looked at the record would wait forever. The timeout fails the test instead of waiting for that hang.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RecordConsumedBeforeWaitersLook_WaitersStillLeaveTheGridBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Blocks = 4;
        const int Threads = 128;
        long cycles = 100L * GpuClock.RateKilohertz();

        // Every thread of block 1 fails at once; the other blocks reach the barrier only after about 100 ms, by which
        // time the consumer below has taken the record
        var keys = new int[Blocks * Threads];
        for (int t = 0; t < Threads; t++)
        {
            keys[Threads + t] = LaunchEpochKernels.FailKey;
        }

        var result = Filled(keys.Length);
        GSharpDeviceErrorSlot consumed = default;
        var consumer = Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (DeviceErrorSlot.TryConsumeSlot(out consumed))
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return false;
        });

        // The launcher finds no record of its own, so the launch returns without an exception
        var launch = Task.Run(() =>
            LaunchEpochKernels.LaunchDelayedGridSyncAfterFailKernel(keys, cycles, result, gridDimX: Blocks,
                blockDimX: Threads));
        await launch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(await consumer, "the consumer found no record");
        Assert.Equal(1u, consumed.BlockX);
        Assert.NotEqual(0u, consumed.Epoch);
        // The barrier never completes, so no thread writes after it
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_LaunchCapturedIntoGraph_ErrorIsThrownAtTheSynchronizationAfterEveryReplay()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        var keys = new int[BlockSize];
        keys[4] = LaunchEpochKernels.FailKey;
        using var dKeys = new GpuBuffer<int>(BlockSize);
        using var dResult = new GpuBuffer<int>(BlockSize);
        dKeys.CopyToDevice(keys);

        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream.Handle, 1));
        IntPtr graph;
        try
        {
            LaunchEpochKernels.LaunchDelayedWriteKernelPtr(dKeys.ToProxy(), 0, dResult.ToProxy(), blockDimX: BlockSize,
                hStream: stream.Handle);
        }
        finally
        {
            CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream.Handle, out graph));
        }

        CudaException.ThrowIfError(CudaDriverApi.cuGraphInstantiate(out var exec, graph, IntPtr.Zero, IntPtr.Zero, 0));
        try
        {
            // The context survives the error, so the graph replays and reports it again
            for (int replay = 0; replay < 2; replay++)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuGraphLaunch(exec, stream.Handle));
                var ex = Assert.Throws<SwitchExpressionException>(stream.Synchronize);
                Assert.Equal("(4,0,0)", ex.Data["GSharp.ThreadIndex"]);
                Assert.Null(ex.InnerException);
            }
        }
        finally
        {
            CudaDriverApi.cuGraphExecDestroy(exec);
            CudaDriverApi.cuGraphDestroy(graph);
        }
    }

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
