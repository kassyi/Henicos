using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Runtime;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Every launch records a device error under its own epoch: its launcher throws only its own error, an error that
/// nothing consumed is thrown by the launch that reuses its record, the waiters at the grid barrier leave on the abort
/// word of their launch even when a host synchronization consumed the record, and a launch captured into a CUDA graph
/// reports its error at the synchronization after the graph launch, its barrier waiters leave on an abort word that the
/// graph clears before every replay, and the context stays usable.
/// </summary>
/// <remarks>
/// [WHY] Sequential: some of these tests leave a record for a synchronization that consumes the record of any launch,
/// or count launches, which a test running beside them would disturb.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class LaunchEpochTests
{
    private const int Untouched = -7;
    private const int BlockSize = 32;

    // A launch reuses the record of the launch this many epochs before it
    private const int LaunchRecords = (int)LaunchErrorRecords.Records;

    // The grid of the graph barrier tests
    private const int GraphBlocks = 4;
    private const int GraphThreads = 128;

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryConsumeSlot_RecordStillBeingWritten_ConsumesItOnlyOnceCompleted()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        GpuKernelLoader.EnsureInitialized();
        // A device thread of a running launch claimed the record of epoch 0x1234 and has not set the completion bit yet
        const uint Epoch = 0x1234u;
        const uint Code = (uint)GSharpErrorCode.IndexOutOfRange;
        var record = LaunchErrorRecords.RecordForTest(Epoch);
        Assert.True(record != null, "the per-launch records were not allocated");
        ref var claim = ref *(ulong*)record;
        Volatile.Write(ref claim, Code | ((ulong)Epoch << 32));
        try
        {
            Assert.False(DeviceErrorSlot.TryConsumeSlot(out _), "a record without its completion bit was consumed");
            Assert.Equal(Code | ((ulong)Epoch << 32), Volatile.Read(ref claim));

            // The writer finishes the record: now it is consumed whole, with its epoch
            record->SiteId = 7u;
            Volatile.Write(ref record->Code, Code | GSharpErrorMapper.CompletionBit);
            Assert.True(DeviceErrorSlot.TryConsumeSlot(out var slot));
            Assert.Equal(Epoch, slot.Epoch);
            Assert.Equal(7u, slot.SiteId);
            Assert.Equal(0ul, Volatile.Read(ref claim));
        }
        finally
        {
            Volatile.Write(ref claim, 0ul);
            record->SiteId = 0u;
        }
    }

    // [WHY] Between the end of a kernel and the read of its launcher's record (after the copy-back) another thread may
    // synchronize something unrelated; a synchronization that took any record would throw the launch's error there and
    // let the launcher return partial results with no exception
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task DeviceErrorSlot_UnrelatedCopyToHostOnAnotherThread_NeverTakesTheLaunchersRecord()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Launches = 200;
        const int Threads = 8 * 1024 * 1024;
        var result = new int[Threads];
        int launcherThrew = 0;
        int copierThrew = 0;
        using var stop = new CancellationTokenSource();

        var copier = Task.Run(() =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            using var buffer = new GpuBuffer<int>(1);
            var host = new int[1];
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    buffer.CopyToHost(host);
                }
                catch (SwitchExpressionException)
                {
                    copierThrew++;
                }
            }
        });

        var launcher = Task.Run(() =>
        {
            for (int i = 0; i < Launches; i++)
            {
                try
                {
                    LaunchEpochKernels.LaunchFillUnlessFailKernel(i % Threads, result, gridDimX: Threads / 256,
                        blockDimX: 256);
                }
                catch (SwitchExpressionException)
                {
                    launcherThrew++;
                }
            }
        });

        await launcher.WaitAsync(TimeSpan.FromMinutes(5));
        await stop.CancelAsync();
        await copier.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(Launches, launcherThrew);
        Assert.Equal(0, copierThrew);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DeviceError_FailingLaunchOnOneOfTwoStreams_OnlyItsStreamThrows()
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
    public void Launcher_FailingLaunch_ConsumesItsOwnRecord()
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
    public void Launcher_UnconsumedRecordOfEarlierLaunch_ThrowsWhenItReusesTheRecord()
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

    // [WHY] A synchronization of another stream consumes the record of a launch that no launcher reads itself (a Ptr
    // launch). The waiters of the launch read its abort word, which no host synchronization clears, so they leave even
    // though the record is gone; a waiter that looked at the record would wait forever. The timeout fails the test
    // instead of waiting for that hang.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GridBarrier_RecordConsumedBeforeWaitersLook_WaitersStillLeave()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        return AssertWaitersLeaveAfterRecordConsumedAsync(captured: false);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GraphLaunch_FailingThread_ThrowsAtTheSynchronizationAfterEveryReplay()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        var keys = new int[BlockSize];
        keys[4] = LaunchEpochKernels.FailKey;
        using var dKeys = new GpuBuffer<int>(BlockSize);
        using var dResult = new GpuBuffer<int>(BlockSize);
        dKeys.CopyToDevice(keys);

        using var graph = CapturedGraph.Capture(stream, hStream =>
            LaunchEpochKernels.LaunchDelayedWriteKernelPtr(dKeys.ToProxy(), 0, dResult.ToProxy(), blockDimX: BlockSize,
                hStream: hStream));

        // The context survives the error, so the graph replays and reports it again
        for (int replay = 0; replay < 2; replay++)
        {
            graph.Launch(stream);
            var ex = Assert.Throws<SwitchExpressionException>(stream.Synchronize);
            Assert.Equal("(4,0,0)", ex.Data["GSharp.ThreadIndex"]);
            Assert.Null(ex.InnerException);
        }
    }

    // [WHY] A captured launch of a kernel with a grid barrier has an abort word of its own, which a kernel node of the
    // graph clears before every replay: when a whole block fails, the waiters leave instead of trapping (a trap is a
    // GPU fault that ends the context), and a replay after a failed one passes every barrier again, which also needs
    // the leaving blocks to take back their arrivals (the graph keeps its grid workspace from one replay to the next).
    // The timeouts fail the test instead of waiting for a hang that the driver would end only by resetting the GPU.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task GraphLaunch_WholeBlockFailsBeforeGridBarrier_WaitersLeaveAtEveryReplay()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var failing = new int[GraphBlocks * GraphThreads];
        for (int t = 0; t < GraphThreads; t++)
        {
            failing[GraphThreads + t] = GridBarrierKernels.FailKey;
        }

        await AssertCapturedFailureLeavesContextUsableAsync(failing, "(1,0,0)", "GSharp.BlockIndex",
            static (keys, result, stream) => GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernelPtr(keys,
                result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    // [WHY] The same for a captured launch, which records into the shared record: the waiters leave on the abort word
    // of the capture epoch, which no host synchronization clears either
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GraphLaunch_RecordConsumedBeforeWaitersLook_WaitersStillLeave()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        return AssertWaitersLeaveAfterRecordConsumedAsync(captured: true);
    }

    // Every thread of block 1 fails at once; the other blocks reach the barrier only after about 100 ms, by which time a
    // consumer on another thread has taken the record. The launch is a Ptr launch, or a graph that captured it; the
    // record names block 1 and the launch's epoch, and no thread writes after the barrier, which never completes.
    private static async Task AssertWaitersLeaveAfterRecordConsumedAsync(bool captured)
    {
        long cycles = 100L * GpuClock.RateKilohertz();
        var keys = new int[GraphBlocks * GraphThreads];
        for (int t = 0; t < GraphThreads; t++)
        {
            keys[GraphThreads + t] = LaunchEpochKernels.FailKey;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        using var dKeys = new GpuBuffer<int>(keys.Length);
        using var dResult = new GpuBuffer<int>(keys.Length);
        dKeys.CopyToDevice(keys);
        dResult.CopyToDevice(Filled(keys.Length));
        Action<IntPtr> launch = hStream => LaunchEpochKernels.LaunchDelayedGridSyncAfterFailKernelPtr(dKeys.ToProxy(),
            cycles, dResult.ToProxy(), gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: hStream);
        using var graph = captured ? CapturedGraph.Capture(stream, launch) : null;

        using var stop = new CancellationTokenSource();
        GSharpDeviceErrorSlot consumed = default;
        var consumer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (DeviceErrorSlot.TryConsumeSlot(out consumed))
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return false;
        });

        try
        {
            var run = Task.Run(() =>
            {
                using var launchScope = new GpuKernelLoader.CudaContextScope();
                if (graph is not null)
                {
                    graph.Launch(stream);
                }
                else
                {
                    launch(stream.Handle);
                }

                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream.Handle));
            });
            await run.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(await consumer.WaitAsync(TimeSpan.FromSeconds(10)), "the consumer found no record");
        }
        finally
        {
            await stop.CancelAsync();
        }

        Assert.Equal(1u, consumed.BlockX);
        // A capture epoch has bit 31 set
        Assert.Equal(captured ? 0x80000000u : 0u, consumed.Epoch & 0x80000000u);
        Assert.NotEqual(0u, consumed.Epoch);
        var result = new int[keys.Length];
        dResult.CopyToHost(result);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    // [WHY] The same for an mbarrier, whose waiters wait for every thread of the block: one failing thread is enough
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task GraphLaunch_ThreadFailsBeforeMBarrier_WaitersLeaveAtEveryReplay()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var failing = new int[BlockSize];
        failing[5] = DeviceErrorExitKernels.FailKey;

        await AssertCapturedFailureLeavesContextUsableAsync(failing, "(5,0,0)", "GSharp.ThreadIndex",
            static (keys, result, stream) => DeviceErrorExitKernels.LaunchMBarrierFailKernelPtr(keys, result,
                blockDimX: BlockSize, hStream: stream));
    }

    // [WHY] The abort word of a captured launch must never make a healthy replay leave its barriers: every replay
    // starts with a cleared word, and no failure sets it
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public async Task GraphLaunch_HealthyBarrierKernels_EveryReplayPassesEveryBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Replays = 200;
        await Task.Run(() =>
        {
            AssertHealthyReplays(GraphBlocks * GraphThreads, Replays,
                static (keys, result, stream) => GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernelPtr(keys,
                    result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
            AssertHealthyReplays(BlockSize, Replays,
                static (keys, result, stream) => DeviceErrorExitKernels.LaunchMBarrierFailKernelPtr(keys, result,
                    blockDimX: BlockSize, hStream: stream));
        }).WaitAsync(TimeSpan.FromMinutes(2));
    }

    // Captures <launch> with the failing keys, replays it twice and checks that each synchronization throws the
    // recorded error with no CUDA error inside and that no thread wrote after the barrier; then replays the same graph
    // with healthy keys, which must pass the barrier, and runs a launch outside the graph, which needs a usable context
    private static async Task AssertCapturedFailureLeavesContextUsableAsync(int[] failing, string failedAt,
        string failedAtKey, Action<GSharpArrayProxy, GSharpArrayProxy, IntPtr> launch)
    {
        await Task.Run(() =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            using var stream = new CudaStream();
            using var dKeys = new GpuBuffer<int>(failing.Length);
            using var dResult = new GpuBuffer<int>(failing.Length);
            dKeys.CopyToDevice(failing);
            var result = new int[failing.Length];

            using (var graph = CapturedGraph.Capture(stream, hStream =>
                launch(dKeys.ToProxy(), dResult.ToProxy(), hStream)))
            {
                for (int replay = 0; replay < 2; replay++)
                {
                    dResult.CopyToDevice(Filled(failing.Length));
                    graph.Launch(stream);
                    var ex = Assert.Throws<SwitchExpressionException>(stream.Synchronize);
                    Assert.Null(ex.InnerException);
                    Assert.Equal(failedAt, ex.Data[failedAtKey]);
                    dResult.CopyToHost(result);
                    Assert.All(result, static value => Assert.Equal(Untouched, value));
                }

                dKeys.CopyToDevice(new int[failing.Length]);
                dResult.CopyToDevice(Filled(failing.Length));
                graph.Launch(stream);
                stream.Synchronize();
                dResult.CopyToHost(result);
                Assert.Equal(Enumerable.Range(0, failing.Length), result);
            }

            var direct = Filled(BlockSize);
            LaunchEpochKernels.LaunchDelayedWriteKernel(new int[BlockSize], 0, direct, blockDimX: BlockSize);
            Assert.Equal(Enumerable.Range(0, BlockSize), direct);
        }).WaitAsync(TimeSpan.FromSeconds(60));
    }

    // Captures <launch> with healthy keys and replays it <replays> times; every replay writes every thread's index
    private static void AssertHealthyReplays(int threads, int replays,
        Action<GSharpArrayProxy, GSharpArrayProxy, IntPtr> launch)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        using var dKeys = new GpuBuffer<int>(threads);
        using var dResult = new GpuBuffer<int>(threads);
        dKeys.CopyToDevice(new int[threads]);
        var result = new int[threads];
        using var graph = CapturedGraph.Capture(stream, hStream => launch(dKeys.ToProxy(), dResult.ToProxy(), hStream));
        for (int replay = 0; replay < replays; replay++)
        {
            dResult.CopyToDevice(Filled(threads));
            graph.Launch(stream);
            stream.Synchronize();
            dResult.CopyToHost(result);
            Assert.Equal(Enumerable.Range(0, threads), result);
        }
    }

    // [WHY] A captured launch of a cooperative kernel has an abort word, like a launch that is not captured: once a
    // thread failed, every block leaves the grid barrier, also when thread (0,0,0) or whole warps failed, so no
    // surviving thread runs the code after the barriers (GridBarrierTests in Tests.Shared, without a graph)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GraphLaunch_ThreadsFailBeforeGridBarrier_EveryBlockLeavesBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        AssertEveryBlockLeavesBarrierOfGraph(GridBarrierKernels.FailKey,
            static (keys, rounds, values, result, stream) => GridBarrierKernels.LaunchGridSyncAfterFailKernelPtr(keys,
                rounds, values, result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    // [WHY] The same for the Fast barrier, which arrives before it reads the abort word: the failing threads of a block
    // set the word before the block elects its arriving thread, so every block leaves at the first barrier
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GraphLaunch_ThreadsFailBeforeFastGridBarrier_EveryBlockLeavesBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        AssertEveryBlockLeavesBarrierOfGraph(GridSyncModeKernels.FailKey,
            static (keys, rounds, values, result, stream) => GridSyncModeKernels.LaunchFastSyncAfterFailKernelPtr(keys,
                rounds, values, result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    // Captures <launch> of a kernel that runs 64 rounds of two grid barriers into a graph, with every third
    // thread failing (thread 0 of every block among them, and in block 2 a whole warp but thread 1), replays it once
    // and checks that the synchronization throws the recorded error and that no surviving thread wrote its sum
    private static void AssertEveryBlockLeavesBarrierOfGraph(int failKey,
        Action<GSharpArrayProxy, int, GSharpArrayProxy, GSharpArrayProxy, IntPtr> launch)
    {
        const int Rounds = 64;

        var keys = new int[GraphBlocks * GraphThreads];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = i % GraphThreads % 3 == 0 ? failKey : i % 2;
        }

        for (int t = 2; t < 32; t++)
        {
            keys[2 * GraphThreads + t] = failKey;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        using var dKeys = new GpuBuffer<int>(keys.Length);
        using var dValues = new GpuBuffer<int>(GraphBlocks);
        using var dResult = new GpuBuffer<int>(keys.Length);
        dKeys.CopyToDevice(keys);
        dResult.CopyToDevice(Filled(keys.Length));

        using (var graph = CapturedGraph.Capture(stream, hStream =>
            launch(dKeys.ToProxy(), Rounds, dValues.ToProxy(), dResult.ToProxy(), hStream)))
        {
            graph.Launch(stream);
            var ex = Assert.Throws<SwitchExpressionException>(stream.Synchronize);
            Assert.Null(ex.InnerException);
        }

        var result = new int[keys.Length];
        dResult.CopyToHost(result);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }

    // A launch captured into a CUDA graph and the graph's executable instance, both destroyed on Dispose
    private sealed class CapturedGraph(IntPtr graph, IntPtr exec) : IDisposable
    {
        // Captures what <launch> enqueues on the stream (the stream handle is its argument) and instantiates it
        public static CapturedGraph Capture(CudaStream stream, Action<IntPtr> launch)
        {
            var begin = CudaDriverApi.cuStreamBeginCapture(stream.Handle, CUstreamCaptureMode.ThreadLocal);
            CudaException.ThrowIfError(begin);
            IntPtr graph;
            try
            {
                launch(stream.Handle);
            }
            finally
            {
                CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream.Handle, out graph));
            }

            var result = CudaDriverApi.cuGraphInstantiate(out var exec, graph, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != CUresult.CudaSuccess)
            {
                CudaDriverApi.cuGraphDestroy(graph);
                CudaException.ThrowIfError(result);
            }

            return new CapturedGraph(graph, exec);
        }

        public void Launch(CudaStream stream) =>
            CudaException.ThrowIfError(CudaDriverApi.cuGraphLaunch(exec, stream.Handle));

        public void Dispose()
        {
            CudaDriverApi.cuGraphExecDestroy(exec);
            CudaDriverApi.cuGraphDestroy(graph);
        }
    }
}
