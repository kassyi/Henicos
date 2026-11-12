using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Runtime;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Launches captured into a CUDA graph whose kernel waits at a barrier that counts a fixed number of arrivals (the
/// grid barrier, an mbarrier): when a thread fails, the waiters leave on an abort word that a node of the graph clears
/// before every replay, the synchronization after the replay throws the recorded error, the context stays usable, and
/// the replays after it run exactly.
/// </summary>
/// <remarks>
/// [WHY] Sequential: a captured launch records into the shared record, which the synchronization of any test running
/// beside it would consume. The timeouts fail a test instead of waiting for a hang that the driver would end only by
/// resetting the GPU.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class CapturedBarrierTests
{
    private const int Untouched = -7;
    private const int BlockSize = 32;
    private const int GraphBlocks = 4;
    private const int GraphThreads = 128;
    private const int HealthyReplays = 200;

    // [WHY] When a whole block fails, the waiters of the other blocks leave instead of trapping (a trap is a GPU fault
    // that ends the context), and a replay after a failed one passes every barrier again, which also needs the leaving
    // blocks to take back their arrivals (the graph keeps its grid workspace from one replay to the next)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GraphLaunch_WholeBlockFailsBeforeGridBarrier_WaitersLeaveAtEveryReplay()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var failing = new int[GraphBlocks * GraphThreads];
        Array.Fill(failing, GridBarrierKernels.FailKey, GraphThreads, GraphThreads);

        return AssertCapturedFailureLeavesContextUsableAsync(failing, "(1,0,0)", "GSharp.BlockIndex",
            static (keys, result, stream) => GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernelPtr(keys,
                result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    // [WHY] The same for an mbarrier, whose waiters wait for every thread of the block: one failing thread is enough
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GraphLaunch_ThreadFailsBeforeMBarrier_WaitersLeaveAtEveryReplay()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var failing = new int[BlockSize];
        failing[5] = DeviceErrorExitKernels.FailKey;

        return AssertCapturedFailureLeavesContextUsableAsync(failing, "(5,0,0)", "GSharp.ThreadIndex",
            static (keys, result, stream) => DeviceErrorExitKernels.LaunchMBarrierFailKernelPtr(keys, result,
                blockDimX: BlockSize, hStream: stream));
    }

    // [WHY] In a healthy replay after a failed one, the first warp waits about 20 ms for thread 33 and reads the abort
    // word while it waits: the word must have been cleared before the replay, or the first warp exits on the failure of
    // the replay before and writes nothing
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public Task GraphLaunch_LateMBarrierArrivalAfterFailedReplay_EveryThreadPassesTheBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Threads = 64;
        long cycles = 20L * GpuClock.RateKilohertz();
        var failing = new int[Threads];
        failing[5] = CapturedBarrierKernels.FailKey;

        return AssertCapturedFailureLeavesContextUsableAsync(failing, "(5,0,0)", "GSharp.ThreadIndex",
            (keys, result, stream) => CapturedBarrierKernels.LaunchDelayedMBarrierKernelPtr(keys, cycles, result,
                blockDimX: Threads, hStream: stream));
    }

    // [WHY] The abort word of a captured launch must never make a healthy replay leave its barriers
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public Task GraphLaunch_HealthyGridBarrier_EveryReplayPassesTheBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        return AssertHealthyReplaysAsync(GraphBlocks * GraphThreads,
            static (keys, result, stream) => GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernelPtr(keys,
                result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public Task GraphLaunch_HealthyMBarrier_EveryReplayPassesTheBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        return AssertHealthyReplaysAsync(BlockSize,
            static (keys, result, stream) => DeviceErrorExitKernels.LaunchMBarrierFailKernelPtr(keys, result,
                blockDimX: BlockSize, hStream: stream));
    }

    // [WHY] Once a thread failed, every block leaves the grid barrier, also when thread (0,0,0) or whole warps failed,
    // so no surviving thread runs the code after the barriers (GridBarrierTests in Tests.Shared, without a graph)
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

    // [WHY] Two captured launches of one graph that fail in the same replay both write the one shared record: the
    // second keeps the first error and leaves instead of trapping, so the synchronization throws one error and the
    // context stays usable
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GraphLaunch_TwoFailingCooperativeNodes_OneErrorAndUsableContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var failing = new int[GraphBlocks * GraphThreads];
        failing[5] = GridBarrierKernels.FailKey;
        return AssertTwoFailingNodesAsync(failing,
            static (keys, result, stream) => GridBarrierKernels.LaunchCooperativeFailKernelPtr(keys, result,
                gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    // [WHY] The same for two grid barrier launches whose block 1 fails, so the waiters of both leave on their words
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public Task GraphLaunch_TwoFailingGridBarrierNodes_OneErrorAndUsableContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var failing = new int[GraphBlocks * GraphThreads];
        Array.Fill(failing, GridBarrierKernels.FailKey, GraphThreads, GraphThreads);
        return AssertTwoFailingNodesAsync(failing,
            static (keys, result, stream) => GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernelPtr(keys,
                result, gridDimX: GraphBlocks, blockDimX: GraphThreads, hStream: stream));
    }

    // [WHY] Failing and healthy replays of one graph in random order, the failure at a random round, so that some
    // barriers complete in a failing replay: every healthy replay must give the exact sums, and every failing one must
    // throw its error with no CUDA error inside. A leaving block that kept its arrival, or a word not cleared before a
    // replay, breaks the barriers of the replays after it.
    [SkippableTheory]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 1655)]
    [InlineData(true, 1656)]
    public Task GraphLaunch_MixedFailingAndHealthyReplays_EveryReplayIsExact(bool fast, int seed)
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        return Task.Run(() => AssertMixedReplays(fast, seed)).WaitAsync(TimeSpan.FromMinutes(3));
    }

    private static void AssertMixedReplays(bool fast, int seed)
    {
        const int Rounds = 6;
        const int Replays = 400;
        const int Threads = GraphBlocks * GraphThreads;
        var random = new Random(seed);
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        using var dKeys = new GpuBuffer<int>(Threads);
        using var dControl = new GpuBuffer<int>(1);
        using var dValues = new GpuBuffer<int>(GraphBlocks);
        using var dResult = new GpuBuffer<int>(Threads);
        var sums = Enumerable.Repeat(Rounds * (Rounds + 1) / 2, Threads).ToArray();
        var result = new int[Threads];
        using var graph = CapturedGraph.Capture(stream, hStream =>
        {
            if (fast)
            {
                CapturedBarrierKernels.LaunchFastFailAtRoundKernelPtr(dKeys.ToProxy(), Rounds, dControl.ToProxy(),
                    dValues.ToProxy(), dResult.ToProxy(), gridDimX: GraphBlocks, blockDimX: GraphThreads,
                    hStream: hStream);
            }
            else
            {
                CapturedBarrierKernels.LaunchFailAtRoundKernelPtr(dKeys.ToProxy(), Rounds, dControl.ToProxy(),
                    dValues.ToProxy(), dResult.ToProxy(), gridDimX: GraphBlocks, blockDimX: GraphThreads,
                    hStream: hStream);
            }
        });

        for (int replay = 0; replay < Replays; replay++)
        {
            bool fails = random.Next(2) == 0;
            dKeys.CopyToDevice(fails ? FailingKeys(random) : new int[Threads]);
            dControl.CopyToDevice([random.Next(Rounds)]);
            dResult.CopyToDevice(Filled(Threads));
            graph.Launch(stream);
            if (fails)
            {
                var ex = Assert.Throws<SwitchExpressionException>(stream.Synchronize);
                Assert.Null(ex.InnerException);
                continue;
            }

            stream.Synchronize();
            dResult.CopyToHost(result);
            Assert.Equal(sums, result);
        }
    }

    // One thread, a whole block, every third thread, or thread 0 of every second block
    private static int[] FailingKeys(Random random)
    {
        var keys = new int[GraphBlocks * GraphThreads];
        switch (random.Next(4))
        {
            case 0:
                keys[random.Next(keys.Length)] = CapturedBarrierKernels.FailKey;
                break;
            case 1:
                Array.Fill(keys, CapturedBarrierKernels.FailKey, random.Next(GraphBlocks) * GraphThreads, GraphThreads);
                break;
            case 2:
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i] = i % GraphThreads % 3 == 0 ? CapturedBarrierKernels.FailKey : 0;
                }

                break;
            default:
                for (int b = 0; b < GraphBlocks; b += 2)
                {
                    keys[b * GraphThreads] = CapturedBarrierKernels.FailKey;
                }

                break;
        }

        return keys;
    }

    // Captures <launch> with the failing keys, replays it twice and checks that each synchronization throws the
    // recorded error with no CUDA error inside and that no thread wrote after the barrier; then replays the same graph
    // with healthy keys, which must pass the barrier, and runs a launch outside the graph, which needs a usable context
    private static Task AssertCapturedFailureLeavesContextUsableAsync(int[] failing, string failedAt,
        string failedAtKey, Action<GSharpArrayProxy, GSharpArrayProxy, IntPtr> launch) =>
        Task.Run(() =>
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

            AssertContextUsable();
        }).WaitAsync(TimeSpan.FromSeconds(60));

    // Captures <launch> twice into one graph, both with the failing keys, and replays it twice: each synchronization
    // throws one recorded error with no CUDA error inside, and a launch outside the graph runs afterwards
    private static Task AssertTwoFailingNodesAsync(int[] failing,
        Action<GSharpArrayProxy, GSharpArrayProxy, IntPtr> launch) =>
        Task.Run(() =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            using var stream = new CudaStream();
            using var dKeys = new GpuBuffer<int>(failing.Length);
            using var dFirst = new GpuBuffer<int>(failing.Length);
            using var dSecond = new GpuBuffer<int>(failing.Length);
            dKeys.CopyToDevice(failing);
            using (var graph = CapturedGraph.Capture(stream, hStream =>
            {
                launch(dKeys.ToProxy(), dFirst.ToProxy(), hStream);
                launch(dKeys.ToProxy(), dSecond.ToProxy(), hStream);
            }))
            {
                for (int replay = 0; replay < 2; replay++)
                {
                    graph.Launch(stream);
                    var ex = Assert.Throws<SwitchExpressionException>(stream.Synchronize);
                    Assert.Null(ex.InnerException);
                    // The second error was not recorded, so nothing is left for the next synchronization
                    stream.Synchronize();
                }
            }

            AssertContextUsable();
        }).WaitAsync(TimeSpan.FromSeconds(60));

    // Captures <launch> with healthy keys and replays it HealthyReplays times; every replay writes every thread's index
    private static Task AssertHealthyReplaysAsync(int threads,
        Action<GSharpArrayProxy, GSharpArrayProxy, IntPtr> launch) =>
        Task.Run(() =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            using var stream = new CudaStream();
            using var dKeys = new GpuBuffer<int>(threads);
            using var dResult = new GpuBuffer<int>(threads);
            dKeys.CopyToDevice(new int[threads]);
            var result = new int[threads];
            using var graph = CapturedGraph.Capture(stream, hStream =>
                launch(dKeys.ToProxy(), dResult.ToProxy(), hStream));
            for (int replay = 0; replay < HealthyReplays; replay++)
            {
                dResult.CopyToDevice(Filled(threads));
                graph.Launch(stream);
                stream.Synchronize();
                dResult.CopyToHost(result);
                Assert.Equal(Enumerable.Range(0, threads), result);
            }
        }).WaitAsync(TimeSpan.FromMinutes(2));

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

        Array.Fill(keys, failKey, 2 * GraphThreads + 2, 30);

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

    private static void AssertContextUsable()
    {
        var direct = Filled(BlockSize);
        LaunchEpochKernels.LaunchDelayedWriteKernel(new int[BlockSize], 0, direct, blockDimX: BlockSize);
        Assert.Equal(Enumerable.Range(0, BlockSize), direct);
    }

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
