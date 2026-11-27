#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[Collection(TestCollections.DeviceExclusive)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class ManagedStackBatonTests
{
    private const string WakeAheadEnvVar = "GSHARP_TEST_STACK_WAKE_AHEAD";
    private static readonly TimeSpan s_joinTimeout = TimeSpan.FromSeconds(60);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("8")]
    public void HandOver_CustomOrderWithBarriers_MatchesExpectedOrderAndCountsOncePerStep(string? wakeAheadSetting)
    {
        const int threads = 8;
        const int barriers = 3;
        const int rounds = barriers + 1;
        var log = new List<(int Round, int Thread)>();
        var roundOrders = new Dictionary<int, int[]>();

        using (SetWakeAheadEnv(wakeAheadSetting))
        using (ManagedStackRunner.UseThreadOrder(round =>
               {
                   int[] order = round.Round switch
                   {
                       1 => [7, 6, 5, 4, 3, 2, 1, 0],
                       2 => [0, 2, 4, 6, 1, 3, 5, 7],
                       3 => [3, 1, 7, 5, 2, 0, 6, 4],
                       _ => [4, 5, 6, 7, 0, 1, 2, 3],
                   };
                   roundOrders[round.Round] = order;
                   return order;
               }))
        {
            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.HandOverCount", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                log.Add((1, local));
                ManagedGpuContext.__gsharp_stack_block_barrier();
                log.Add((2, local));
                ManagedGpuContext.__gsharp_stack_block_barrier();
                log.Add((3, local));
                ManagedGpuContext.__gsharp_stack_block_barrier();
                log.Add((4, local));
            });

            Assert.Equal((long)(threads * (barriers + 1)) + rounds, ManagedStackRunner.LastLaunchHandOverCountOnThisThread);
        }

        var expected = new List<(int Round, int Thread)>();
        for (int r = 1; r <= rounds; r++)
        {
            foreach (int t in roundOrders[r])
            {
                expected.Add((r, t));
            }
        }

        Assert.Equal(expected, log);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void WakeAhead_BarrierKernelWithEightAhead_MatchesZeroAheadOrderAndCountsSignals()
    {
        const int threads = 32;
        var zeroOrder = RunBarrierWithEarlyExit(threads, "0", out long zeroWakeAhead);
        var eightOrder = RunBarrierWithEarlyExit(threads, "8", out long eightWakeAhead);

        Assert.Equal(zeroOrder, eightOrder);
        Assert.Equal(0L, zeroWakeAhead);
        if (ManagedStackRunner.BoundWakeAheadCount(8, threads, Environment.ProcessorCount) > 0)
        {
            Assert.True(eightWakeAhead > 0L, "Expected wake-ahead signals to be sent when bounded k > 0.");
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void WakeAhead_WarpCollectiveAndActiveMask_MatchesZeroAheadOrderAndValues()
    {
        const int threads = 32;
        var zeroResult = RunWarpAndActiveMask(threads, "0", out var zeroSteps, out long zeroWakeAhead);
        var eightResult = RunWarpAndActiveMask(threads, "8", out var eightSteps, out long eightWakeAhead);

        Assert.Equal(zeroSteps, eightSteps);
        Assert.Equal(zeroResult.Values, eightResult.Values);
        Assert.Equal(zeroResult.Masks, eightResult.Masks);
        Assert.Equal(0L, zeroWakeAhead);
        if (ManagedStackRunner.BoundWakeAheadCount(8, threads, Environment.ProcessorCount) > 0)
        {
            Assert.True(eightWakeAhead > 0L, "Expected wake-ahead signals on warp kernel when bounded k > 0.");
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void WakeAhead_MidRoundFailure_RunsRemainingThreadsReportsFirstFailureAndReturnsWokenWorkers()
    {
        const int threads = 32;
        var zeroSteps = new List<(int Round, int Thread)>();
        var eightSteps = new List<(int Round, int Thread)>();

        using (SetWakeAheadEnv("8"))
        {
            // Warm up the pool under the wake-ahead mode so thread counts stay stable across the failing launch.
            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.FailureWarmup", _ =>
            {
                ManagedGpuContext.__gsharp_stack_block_barrier();
            });

            int idleBefore = ManagedStackRunner.IdleThreadCount;
            int createdBefore = ManagedStackRunner.CreatedThreadCount;

            var eightEx = RunMidRoundFailureKernel(threads, eightSteps, out long eightWakeAhead);
            Assert.Equal("first mid-round failure", eightEx.Message);
            Assert.Equal(idleBefore, ManagedStackRunner.IdleThreadCount);
            if (ManagedStackRunner.BoundWakeAheadCount(8, threads, Environment.ProcessorCount) > 0)
            {
                Assert.True(eightWakeAhead > 0L, "Expected wake-ahead signals before mid-round failure unwind.");
            }

            // Follow-up launch must reuse all returned workers with no stale wake-ahead signal corruption.
            var scratch = new int[threads];
            var output = new int[threads];
            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.AfterWakeAheadFailure", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                scratch[local] = local + 100;
                ManagedGpuContext.__gsharp_stack_block_barrier();
                output[local] = scratch[threads - 1 - local];
            });

            Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
            for (int t = 0; t < threads; t++)
            {
                Assert.Equal(threads - 1 - t + 100, output[t]);
            }
        }

        using (SetWakeAheadEnv("0"))
        {
            var zeroEx = RunMidRoundFailureKernel(threads, zeroSteps, out long zeroWakeAhead);
            Assert.Equal("first mid-round failure", zeroEx.Message);
            Assert.Equal(0L, zeroWakeAhead);
        }

        Assert.Equal(zeroSteps, eightSteps);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void WakeAhead_TwoThreadBlockWithEightAhead_ClampsToZeroAndCompletes()
    {
        const int threads = 2;
        const int barriers = 2;
        var scratch = new int[threads];
        var output = new int[threads];

        using (SetWakeAheadEnv("8"))
        {
            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.TwoThreadWakeAhead", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                scratch[local] = local + 1;
                ManagedGpuContext.__gsharp_stack_block_barrier();
                int partner = scratch[1 - local];
                ManagedGpuContext.__gsharp_stack_block_barrier();
                output[local] = partner * 10 + local;
            });

            Assert.Equal(0L, ManagedStackRunner.LastLaunchWakeAheadCountOnThisThread);
            Assert.Equal((long)(threads * (barriers + 1)) + (barriers + 1), ManagedStackRunner.LastLaunchHandOverCountOnThisThread);
        }

        Assert.Equal([20, 11], output);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(8, 2, 24, 0)]
    [InlineData(0, 32, 24, 0)]
    [InlineData(8, 32, 2, 0)]
    [InlineData(8, 32, 6, 4)]
    [InlineData(8, 32, 24, 8)]
    [InlineData(8, 16, 24, 4)]
    public void WakeAheadBound_VariousBlockAndProcessorCounts_ClampsToQuarterBlockAndSpareCores(
        int configuredK, int threadsPerBlock, int processorCount, int expected)
    {
        Assert.Equal(expected, ManagedStackRunner.BoundWakeAheadCount(configuredK, threadsPerBlock, processorCount));
    }

    [SkippableTheory]
    [Platform(TestPlatform.Windows)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(null)]
    [InlineData("8")]
    public void StaScheduler_MultiBarrierAcrossWaitModes_MatchesMtaResult(string? wakeAheadSetting)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Only Windows threads have a COM apartment.");

        using (SetWakeAheadEnv(wakeAheadSetting))
        {
            var mta = RunMultiBarrierOnApartment(ApartmentState.MTA);
            var sta = RunMultiBarrierOnApartment(ApartmentState.STA);
            Assert.Equal(mta, sta);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PoolWarmup_ManyMissingThreads_StartsInParallelAndReusesWithoutLeaks()
    {
        const uint threads = 32;
        var pool = ManagedStackThreadPool.Instance;

        using (pool.BorrowIdleThreadsForTest())
        {
            int createdBefore = pool.CreatedThreadCount;
            var scratch = new int[threads];
            var output = new int[threads];

            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.ParallelWarmupFirst", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                scratch[local] = local + 7;
                ManagedGpuContext.__gsharp_stack_block_barrier();
                output[local] = scratch[(int)threads - 1 - local];
            });

            Assert.Equal(createdBefore + (int)threads, pool.CreatedThreadCount);
            for (int t = 0; t < threads; t++)
            {
                Assert.Equal((int)threads - 1 - t + 7, output[t]);
            }

            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.ParallelWarmupReuse", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                scratch[local] = local + 13;
                ManagedGpuContext.__gsharp_stack_block_barrier();
                output[local] = scratch[(int)threads - 1 - local];
            });

            Assert.Equal(createdBefore + (int)threads, pool.CreatedThreadCount);
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("8")]
    public void CompiledKernels_WakeAheadSettings_ProduceIdenticalResults(string? wakeAheadSetting)
    {
        const int n = 32;
        var input = new int[n];
        for (int i = 0; i < n; i++)
        {
            input[i] = (i * 5) + 3;
        }

        var barrierOut = new int[n];
        var warpOut = new int[n];
        var masks = new uint[n];

        var expectedBarrier = (int[])input.Clone();
        for (int round = 0; round < 3; round++)
        {
            var next = new int[n];
            for (int i = 0; i < n; i++)
            {
                next[i] = expectedBarrier[i] + expectedBarrier[(i + round + 1) % n];
            }

            expectedBarrier = next;
        }

        var expectedMasks = new uint[n];
        var expectedWarp = new int[n];
        var afterActive = new int[n];
        for (int i = 0; i < n; i++)
        {
            expectedMasks[i] = (i & 1) == 0 ? 0x55555555u : 0u;
            afterActive[i] = (i & 1) == 0 ? input[i] + input[i ^ 2] : input[i];
        }

        for (int i = 0; i < n; i++)
        {
            expectedWarp[i] = afterActive[i ^ 1] + 0xFF;
        }

        using (SetWakeAheadEnv(wakeAheadSetting))
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            ManagedStackBatonKernels.LaunchMultiBarrierKernel(input, barrierOut, n, blockDimX: n);
            ManagedStackBatonKernels.LaunchWarpDivergenceAndBarrierKernel(input, warpOut, masks, blockDimX: n);
        }

        Assert.Equal(expectedBarrier, barrierOut);
        Assert.Equal(expectedMasks, masks);
        Assert.Equal(expectedWarp, warpOut);
    }

    private static List<(int Round, int Thread)> RunBarrierWithEarlyExit(
        int threads, string wakeAheadSetting, out long wakeAheadSignals)
    {
        var steps = new List<(int Round, int Thread)>();
        var random = new Random(42);
        using (SetWakeAheadEnv(wakeAheadSetting))
        using (ManagedStackRunner.UseThreadOrder(round =>
               {
                   var order = Enumerable.Range(0, round.ThreadCount).ToArray();
                   random.Shuffle(order);
                   return order;
               }))
        {
            ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0, "Test.WakeAheadBarrierOrder", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                steps.Add((1, local));
                ManagedGpuContext.__gsharp_stack_block_barrier();
                steps.Add((2, local));
                if (local % 5 == 0)
                {
                    return;
                }

                ManagedGpuContext.__gsharp_stack_block_barrier();
                steps.Add((3, local));
                ManagedGpuContext.__gsharp_stack_block_barrier();
                steps.Add((4, local));
            });

            wakeAheadSignals = ManagedStackRunner.LastLaunchWakeAheadCountOnThisThread;
        }

        return steps;
    }

    private static (int[] Values, uint[] Masks) RunWarpAndActiveMask(
        int threads, string wakeAheadSetting, out List<(int Round, int Thread)> steps, out long wakeAheadSignals)
    {
        var recordedSteps = new List<(int Round, int Thread)>();
        var values = new int[threads];
        var masks = new uint[threads];
        var random = new Random(17);

        using (SetWakeAheadEnv(wakeAheadSetting))
        using (ManagedStackRunner.UseThreadOrder(round =>
               {
                   var order = Enumerable.Range(0, round.ThreadCount).ToArray();
                   random.Shuffle(order);
                   return order;
               }))
        {
            ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0, "Test.WakeAheadWarpOrder", reachesWarpCollective: true,
                tid =>
                {
                    int local = ManagedGpuContext.ThreadIdxX;
                    ManagedGpuContext.__gsharp_stack_warp_begin(4);
                    recordedSteps.Add((1, local));
                    ManagedGpuContext.__gsharp_stack_warp_region_enter(0, 0);
                    int v = local * 3 + 1;
                    if ((local & 1) == 0)
                    {
                        masks[tid] = ManagedGpuContext.__gsharp_stack_warp_active_mask(0, 0, "Test(1,1)");
                        recordedSteps.Add((2, local));
                        v += ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 1, "Test(2,1)", 0x55555555u, v, 2);
                    }

                    ManagedGpuContext.__gsharp_stack_warp_reconverge(0, 0, "Test(3,1)");
                    recordedSteps.Add((3, local));
                    ManagedGpuContext.__gsharp_stack_block_barrier();
                    recordedSteps.Add((4, local));
                    values[tid] = v + ManagedGpuContext.__gsharp_stack_warp_reduce_add_sync(0, 2, "Test(4,1)", 0xFFFFFFFFu, v);
                });

            wakeAheadSignals = ManagedStackRunner.LastLaunchWakeAheadCountOnThisThread;
        }

        steps = recordedSteps;
        return (values, masks);
    }

    private static InvalidOperationException RunMidRoundFailureKernel(
        int threads, List<(int Round, int Thread)> steps, out long wakeAheadSignals)
    {
        var random = new Random(99);
        int[]? round1Order = null;

        using (ManagedStackRunner.UseThreadOrder(round =>
               {
                   var order = Enumerable.Range(0, round.ThreadCount).ToArray();
                   random.Shuffle(order);
                   if (round.Round == 1)
                   {
                       round1Order = order;
                   }

                   return order;
               }))
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0, "Test.MidRoundFailure", _ =>
                {
                    int local = ManagedGpuContext.ThreadIdxX;
                    steps.Add((1, local));
                    int[] order = round1Order ?? throw new InvalidOperationException("Round 1 order was not captured.");
                    if (local == order[3])
                    {
                        throw new InvalidOperationException("first mid-round failure");
                    }

                    if (local == order[11])
                    {
                        throw new ArgumentException("second mid-round failure");
                    }

                    ManagedGpuContext.__gsharp_stack_block_barrier();
                    steps.Add((2, local));
                    ManagedGpuContext.__gsharp_stack_block_barrier();
                    steps.Add((3, local));
                }));

            wakeAheadSignals = ManagedStackRunner.LastLaunchWakeAheadCountOnThisThread;
            return ex;
        }
    }

    private static int[] RunMultiBarrierOnApartment(ApartmentState apartment)
    {
        const int threads = 8;
        var output = new int[threads];
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var scratch = new int[threads];
                ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.StaMultiBarrier", _ =>
                {
                    int local = ManagedGpuContext.ThreadIdxX;
                    int acc = local + 1;
                    for (int round = 0; round < 3; round++)
                    {
                        scratch[local] = acc;
                        ManagedGpuContext.__gsharp_stack_block_barrier();
                        acc += scratch[(local + round + 1) % threads];
                        ManagedGpuContext.__gsharp_stack_block_barrier();
                    }

                    output[local] = acc;
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(apartment);
        }

        thread.Start();
        Assert.True(thread.Join(s_joinTimeout));
        Assert.Null(failure);
        return output;
    }

    private static IDisposable SetWakeAheadEnv(string? value)
    {
        string? previous = Environment.GetEnvironmentVariable(WakeAheadEnvVar);
        Environment.SetEnvironmentVariable(WakeAheadEnvVar, value);
        return new EnvScope(WakeAheadEnvVar, previous);
    }

    private sealed class EnvScope(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }
}
