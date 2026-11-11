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

    // [WHY] While every record is owned, a launch takes an epoch of the shared range (bit 31) and records into the
    // shared record: its launcher throws the error, the waiters of its grid barrier leave on its own abort word, and the
    // next launch of the same kernel runs completely
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public Task SharedEpoch_EveryRecordHeld_LauncherThrowsAndWaitersLeave()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[GraphBlocks * GraphThreads];
        Array.Fill(keys, GridBarrierKernels.FailKey, GraphThreads, GraphThreads);
        return Task.Run(() =>
        {
            using var held = LaunchErrorRecords.HoldEveryRecordForTest();
            var result = Filled(keys.Length);
            var ex = Assert.Throws<SwitchExpressionException>(() =>
                GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(keys, result, gridDimX: GraphBlocks,
                    blockDimX: GraphThreads));
            Assert.Null(ex.InnerException);
            Assert.Equal("(1,0,0)", ex.Data["GSharp.BlockIndex"]);
            Assert.All(result, static value => Assert.Equal(Untouched, value));

            var healthy = Filled(keys.Length);
            GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(new int[keys.Length], healthy,
                gridDimX: GraphBlocks, blockDimX: GraphThreads);
            Assert.Equal(Enumerable.Range(0, keys.Length), healthy);
        }).WaitAsync(TimeSpan.FromSeconds(60));
    }

    // [WHY] Two launches of the shared range that fail before a synchronization both write the shared record: the
    // second keeps the first error and leaves instead of trapping, and the record carries an epoch with bit 31 set
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task SharedEpoch_TwoFailingLaunchesBeforeSynchronization_OneErrorAndUsableContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[GraphBlocks * GraphThreads];
        Array.Fill(keys, GridBarrierKernels.FailKey, GraphThreads, GraphThreads);
        return Task.Run(() =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            using var stream = new CudaStream();
            using var dKeys = new GpuBuffer<int>(keys.Length);
            using var dResult = new GpuBuffer<int>(keys.Length);
            dKeys.CopyToDevice(keys);
            using (LaunchErrorRecords.HoldEveryRecordForTest())
            {
                for (int launch = 0; launch < 2; launch++)
                {
                    GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernelPtr(dKeys.ToProxy(), dResult.ToProxy(),
                        gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream.Handle);
                }
            }

            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream.Handle));
            Assert.True(DeviceErrorSlot.TryConsumeSlot(out var slot), "no launch recorded its error");
            Assert.Equal(1u, slot.BlockX);
            Assert.Equal(0x80000000u, slot.Epoch & 0x80000000u);
            Assert.False(DeviceErrorSlot.TryConsumeSlot(out _), "the second error was recorded as well");
            var direct = Filled(BlockSize);
            LaunchEpochKernels.LaunchDelayedWriteKernel(new int[BlockSize], 0, direct, blockDimX: BlockSize);
            Assert.Equal(Enumerable.Range(0, BlockSize), direct);
        }).WaitAsync(TimeSpan.FromSeconds(60));
    }

    // [WHY] The counter of the shared range wraps around after 2^31 epochs, and the epoch then comes back while its
    // abort word may still hold it from a failed launch. The launcher clears the word before a launch that waits for
    // arrivals, so a healthy launch with the same epoch passes its barrier instead of leaving on the old failure.
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public Task SharedEpoch_CounterWrapsAfterFailedLaunch_HealthyLaunchPassesTheBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[GraphBlocks * GraphThreads];
        Array.Fill(keys, GridBarrierKernels.FailKey, GraphThreads, GraphThreads);
        return Task.Run(() =>
        {
            using var held = LaunchErrorRecords.HoldEveryRecordForTest();
            // The counter wraps from int.MaxValue to int.MinValue: both launches take the epoch 0x80000000
            LaunchErrorRecords.SetLastSharedEpochForTest(int.MaxValue);
            Assert.Throws<SwitchExpressionException>(() =>
                GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(keys, Filled(keys.Length),
                    gridDimX: GraphBlocks, blockDimX: GraphThreads));

            LaunchErrorRecords.SetLastSharedEpochForTest(int.MaxValue);
            var healthy = Filled(keys.Length);
            GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(new int[keys.Length], healthy,
                gridDimX: GraphBlocks, blockDimX: GraphThreads);
            Assert.Equal(Enumerable.Range(0, keys.Length), healthy);
        }).WaitAsync(TimeSpan.FromSeconds(60));
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

    // [WHY] The same for a captured launch, which records into the shared record: the waiters leave on the abort word
    // of the capture epoch, which no host synchronization clears either
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GraphLaunch_RecordConsumedBeforeWaitersLook_WaitersStillLeave()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        return AssertWaitersLeaveAfterRecordConsumedAsync(captured: true);
    }

    // Every thread of block 1 fails at once; the other blocks reach the barrier only after about 100 ms, by which time
    // a consumer on another thread has taken the record. The launch is a Ptr launch, or a graph that captured it; the
    // record names block 1 and the launch's epoch, and no thread writes after the barrier, which never completes.
    private static async Task AssertWaitersLeaveAfterRecordConsumedAsync(bool captured)
    {
        long cycles = 100L * GpuClock.RateKilohertz();
        var keys = new int[GraphBlocks * GraphThreads];
        Array.Fill(keys, LaunchEpochKernels.FailKey, GraphThreads, GraphThreads);
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
        var consumer = ConsumeFirstRecordAsync(stop.Token);
        try
        {
            await LaunchAndSynchronizeAsync(stream, graph, launch).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await stop.CancelAsync();
        }

        var consumed = await consumer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(consumed.HasValue, "the consumer found no record");
        Assert.Equal(1u, consumed.Value.BlockX);
        // An epoch with bit 31 set records into the shared record, as a captured launch does
        Assert.Equal(captured ? 0x80000000u : 0u, consumed.Value.Epoch & 0x80000000u);
        Assert.NotEqual(0u, consumed.Value.Epoch);
        var result = new int[keys.Length];
        dResult.CopyToHost(result);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    // Consumes the first device error record that any launch leaves, on another thread, until stop is requested
    private static Task<GSharpDeviceErrorSlot?> ConsumeFirstRecordAsync(CancellationToken stop) =>
        Task.Run<GSharpDeviceErrorSlot?>(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (DeviceErrorSlot.TryConsumeSlot(out var slot))
                {
                    return slot;
                }

                Thread.Sleep(1);
            }

            return null;
        });

    // Launches the graph, or <launch> when it is null, on another thread and waits for the stream through the driver
    // alone, which consumes no record
    private static Task LaunchAndSynchronizeAsync(CudaStream stream, CapturedGraph graph, Action<IntPtr> launch) =>
        Task.Run(() =>
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

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
