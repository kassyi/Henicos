#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class ManagedStackRunnerTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_BarrierBetweenWriteAndRead_EveryThreadWritesBeforeAnyReads()
    {
        const uint blocks = 2;
        const uint threadsPerBlock = 8;
        var scratch = new int[blocks * threadsPerBlock];
        var output = new int[blocks * threadsPerBlock];

        ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.BarrierWriteRead", tid =>
        {
            int block = ManagedGpuContext.BlockIdxX;
            int local = ManagedGpuContext.ThreadIdxX;
            int blockBase = block * (int)threadsPerBlock;
            scratch[blockBase + local] = (block + 1) * 100 + local;
            ManagedGpuContext.__gsharp_stack_block_barrier();
            int neighbor = (local + 1) % (int)threadsPerBlock;
            output[blockBase + local] = scratch[blockBase + neighbor];
        });

        for (int b = 0; b < blocks; b++)
        {
            for (int t = 0; t < threadsPerBlock; t++)
            {
                int neighbor = (t + 1) % (int)threadsPerBlock;
                Assert.Equal((b + 1) * 100 + neighbor, output[b * (int)threadsPerBlock + t]);
            }
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_ThreeBarriers_StepsThreadsInIndexOrderEachRound()
    {
        const uint blocks = 2;
        const uint threadsPerBlock = 4;
        var log = new List<(int Block, int Segment, int Thread)>();

        ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.ThreeBarriers", _ =>
        {
            int block = ManagedGpuContext.BlockIdxX;
            int local = ManagedGpuContext.ThreadIdxX;
            log.Add((block, 0, local));
            ManagedGpuContext.__gsharp_stack_block_barrier();
            log.Add((block, 1, local));
            ManagedGpuContext.__gsharp_stack_block_barrier();
            log.Add((block, 2, local));
            ManagedGpuContext.__gsharp_stack_block_barrier();
            log.Add((block, 3, local));
        });

        var expected = new List<(int Block, int Segment, int Thread)>();
        for (int b = 0; b < blocks; b++)
        {
            for (int s = 0; s < 4; s++)
            {
                for (int t = 0; t < threadsPerBlock; t++)
                {
                    expected.Add((b, s, t));
                }
            }
        }

        Assert.Equal(expected, log);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_ThreadsEndAtDifferentBarriers_LaterBarriersReleaseTheRest()
    {
        const uint threads = 6;
        var completedBarriers = new int[threads];

        ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.EarlyExit", tid =>
        {
            int local = ManagedGpuContext.ThreadIdxX;
            for (int b = 0; b < local; b++)
            {
                ManagedGpuContext.__gsharp_stack_block_barrier();
                completedBarriers[local]++;
            }
        });

        for (int t = 0; t < threads; t++)
        {
            Assert.Equal(t, completedBarriers[t]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_GridAndBlockDimensions_EachThreadSeesItsCoordinates()
    {
        const uint gridX = 2;
        const uint gridY = 3;
        const uint gridZ = 1;
        const uint blockX = 2;
        const uint blockY = 2;
        const uint blockZ = 1;
        var geometry = new LaunchGeometry(gridX, gridY, gridZ, blockX, blockY, blockZ);
        var observed = new GpuThreadContext[geometry.ThreadCount];

        ManagedStackRunner.Run(gridX, gridY, gridZ, blockX, blockY, blockZ, 0, "Test.Dimensions", tid =>
        {
            observed[tid] = new GpuThreadContext(
                ManagedGpuContext.ThreadIdxX, ManagedGpuContext.ThreadIdxY, ManagedGpuContext.ThreadIdxZ,
                ManagedGpuContext.BlockIdxX, ManagedGpuContext.BlockIdxY, ManagedGpuContext.BlockIdxZ,
                ManagedGpuContext.BlockDimX, ManagedGpuContext.BlockDimY, ManagedGpuContext.BlockDimZ,
                ManagedGpuContext.GridDimX, ManagedGpuContext.GridDimY, ManagedGpuContext.GridDimZ);
        });

        for (long id = 0; id < geometry.ThreadCount; id++)
        {
            Assert.Equal(geometry.ContextOf(id), observed[id]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_SharedMemoryAcrossBarrier_OtherThreadReadsTheSameRegion()
    {
        const ulong siteKey = 0x123456789ABCDEF0UL;
        const uint blocks = 2;
        const uint threadsPerBlock = 4;
        var results = new int[blocks];

        ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.SharedMemory", _ =>
        {
            int block = ManagedGpuContext.BlockIdxX;
            int local = ManagedGpuContext.ThreadIdxX;
            var shared = ManagedGpuContext.AllocateSharedMemory<int>(siteKey, 4);
            if (local == 0)
            {
                shared[0] = (block + 1) * 42;
            }

            ManagedGpuContext.__gsharp_stack_block_barrier();
            if (local == 1)
            {
                results[block] = ManagedGpuContext.AllocateSharedMemory<int>(siteKey, 4)[0];
            }
        });

        Assert.Equal([42, 84], results);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_TwoThreadsFailInOneRound_RethrowsTheFirstInOrder()
    {
        var ranAfter = new bool[8];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedStackRunner.Run(1, 1, 1, 8, 1, 1, 0, "Test.TwoFailures", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                if (local == 2)
                {
                    ThrowFirstFailure();
                }

                if (local == 5)
                {
                    throw new ArgumentException("second failure");
                }

                ranAfter[local] = true;
            }));

        Assert.Equal("first failure", ex.Message);
        Assert.NotNull(ex.StackTrace);
        Assert.Contains(nameof(ThrowFirstFailure), ex.StackTrace, StringComparison.Ordinal);
        for (int t = 0; t < 8; t++)
        {
            Assert.Equal(t is not (2 or 5), ranAfter[t]);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_ThreadFailsAfterBarrier_OtherThreadsPassLaterBarriers()
    {
        var output = new long[8];

        var ex = Assert.Throws<IndexOutOfRangeException>(() =>
            ManagedStackRunner.Run(1, 1, 1, 8, 1, 1, 0, "Test.FailAfterBarrier", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                Span<long> acc = stackalloc long[1];
                acc[0] = local;
                ManagedGpuContext.__gsharp_stack_block_barrier();
                acc[0] += 10;
                ManagedGpuContext.__gsharp_stack_block_barrier();
                if (local == 3)
                {
                    int[] empty = [];
                    acc[0] += empty[local];
                }

                ManagedGpuContext.__gsharp_stack_block_barrier();
                output[local] = acc[0] + 100;
            }));

        Assert.IsType<IndexOutOfRangeException>(ex);
        for (int t = 0; t < 8; t++)
        {
            Assert.Equal(t == 3 ? 0L : t + 110L, output[t]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_CustomOrder_FollowsTheOrderOfEachRound()
    {
        var log = new List<(int Round, int Thread)>();

        using (ManagedStackRunner.UseThreadOrder(round => round.Round switch
        {
            1 => [3, 2, 1, 0],
            _ => [0, 2, 1, 3]
        }))
        {
            ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.CustomOrder", _ =>
            {
                int local = ManagedGpuContext.ThreadIdxX;
                log.Add((1, local));
                ManagedGpuContext.__gsharp_stack_block_barrier();
                log.Add((2, local));
            });
        }

        Assert.Equal(
            [(1, 3), (1, 2), (1, 1), (1, 0), (2, 0), (2, 2), (2, 1), (2, 3)],
            log);

        var defaultLog = new List<int>();
        ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.DefaultOrderRestored", _ =>
        {
            defaultLog.Add(ManagedGpuContext.ThreadIdxX);
        });
        Assert.Equal([0, 1, 2, 3], defaultLog);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(new[] { 0, 1, 2 })]
    [InlineData(new[] { 0, 1, 1, 2 })]
    [InlineData(new[] { 0, 1, 2, 4 })]
    public void Run_OrderIsNotAPermutation_ThrowsBeforeAnyThreadRuns(int[] badOrder)
    {
        var bodyCalls = 0;

        using (ManagedStackRunner.UseThreadOrder(_ => badOrder))
        {
            Assert.Throws<InvalidOperationException>(() =>
                ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.BadOrder", _ =>
                {
                    Interlocked.Increment(ref bodyCalls);
                }));
        }

        Assert.Equal(0, bodyCalls);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_OrderFailsInSecondRound_UnwindsTheWaitingThreads()
    {
        const uint threads = 4;
        var finallyCounts = new int[threads];
        var afterBarrierCounts = new int[threads];

        using (ManagedStackRunner.UseThreadOrder(round =>
                   round.Round == 1 ? [0, 1, 2, 3] : [0, 0, 1, 2]))
        {
            Assert.Throws<InvalidOperationException>(() =>
                ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.OrderFailsRound2", _ =>
                {
                    int local = ManagedGpuContext.ThreadIdxX;
                    try
                    {
                        ManagedGpuContext.__gsharp_stack_block_barrier();
                        afterBarrierCounts[local]++;
                    }
                    finally
                    {
                        finallyCounts[local]++;
                    }
                }));
        }

        for (int t = 0; t < threads; t++)
        {
            Assert.Equal(1, finallyCounts[t]);
            Assert.Equal(0, afterBarrierCounts[t]);
        }

        var nextOutput = new int[threads];
        ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.NextAfterCancel", _ =>
        {
            int local = ManagedGpuContext.ThreadIdxX;
            nextOutput[local] = local + 10;
            ManagedGpuContext.__gsharp_stack_block_barrier();
            nextOutput[local] += 100;
        });

        Assert.Equal([110, 111, 112, 113], nextOutput);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_ThreadFailedThenLaunchFails_RethrowsTheThreadFailure()
    {
        using (ManagedStackRunner.UseThreadOrder(round =>
                   round.Round == 1 ? [0, 1, 2, 3] : [0, 0, 1, 2]))
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.ThreadAndLaunchFail", _ =>
                {
                    if (ManagedGpuContext.ThreadIdxX == 1)
                    {
                        throw new ArgumentException("thread failed first");
                    }

                    ManagedGpuContext.__gsharp_stack_block_barrier();
                }));

            Assert.Equal("thread failed first", ex.Message);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_BlockOf1024Threads_RunsEveryThread()
    {
        const uint threads = 1024;
        var scratch = new int[threads];
        var output = new int[threads];

        ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.MaxBlock", _ =>
        {
            int local = ManagedGpuContext.ThreadIdxX;
            scratch[local] = local;
            ManagedGpuContext.__gsharp_stack_block_barrier();
            int next = scratch[(local + 1) & 1023];
            ManagedGpuContext.__gsharp_stack_block_barrier();
            scratch[local] = (scratch[local] + next) ^ 0x55;
            ManagedGpuContext.__gsharp_stack_block_barrier();
            output[local] = scratch[local];
        });

        for (int t = 0; t < threads; t++)
        {
            int expected = (t + ((t + 1) & 1023)) ^ 0x55;
            Assert.Equal(expected, output[t]);
        }
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, 1u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 0u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 0u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 0u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 0u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 1u, 0u)]
    public void Run_ZeroDimension_ThrowsArgumentOutOfRange(
        uint gridX, uint gridY, uint gridZ, uint blockX, uint blockY, uint blockZ)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedStackRunner.Run(gridX, gridY, gridZ, blockX, blockY, blockZ, 0, "Test.ZeroDim", _ => { }));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_AfterTheLaunch_LeavesNoContextOnTheCaller()
    {
        ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.CallerClean", _ =>
        {
            Assert.True(ManagedGpuContext.IsActive);
            ManagedGpuContext.__gsharp_stack_block_barrier();
        });

        Assert.False(ManagedGpuContext.IsActive);
        Assert.False(ManagedThreadContext.IsCSharpExecution);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void BlockBarrier_OutsideALaunch_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(ManagedGpuContext.__gsharp_stack_block_barrier);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Run_OnAnStaThread_GivesTheResultOfAnMtaThread()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Only Windows threads have a COM apartment.");

        var mta = RunReverseOn(ApartmentState.MTA);
        var sta = RunReverseOn(ApartmentState.STA);

        Assert.Equal([3, 2, 1, 0], mta);
        Assert.Equal(mta, sta);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_CallerSetsAnAsyncLocal_NewWorkersDoNotSeeIt()
    {
        var seen = new string?[8];

        WhileAFullBlockIsHeld(() =>
        {
            s_callerValue.Value = "caller";
            try
            {
                ManagedStackRunner.Run(1, 1, 1, 8, 1, 1, 0, "Test.AsyncLocal", _ =>
                {
                    seen[ManagedGpuContext.ThreadIdxX] = s_callerValue.Value;
                });
            }
            finally
            {
                s_callerValue.Value = null;
            }
        });

        Assert.All(seen, value => Assert.Null(value));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_MoreWorkersReturnedThanTheIdleLimit_KeepsAtMostTheLimit()
    {
        WhileAFullBlockIsHeld(() =>
            ManagedStackRunner.Run(1, 1, 1, 8, 1, 1, 0, "Test.OverIdleLimit", _ =>
            {
                ManagedGpuContext.__gsharp_stack_block_barrier();
            }));

        Assert.InRange(ManagedStackRunner.IdleThreadCount, 0, ManagedStackThreadPool.MaxIdleThreads);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_MoreThreadsThanTheManagedPathHolds_ThrowsBeforeAnyRound()
    {
        // Array.MaxLength / 1024 is 2097151 blocks of 1024 threads; one more is over the limit of the managed path
        using (ManagedStackRunner.UseThreadOrder(_ => throw new InvalidOperationException("a round started")))
        {
            Assert.Throws<NotSupportedException>(() =>
                ManagedStackRunner.Run(2097152, 1, 1, 1024, 1, 1, 0, "Test.TooManyThreads", _ => { }));
        }
    }

    // The warp collectives of the stack model, called as a stack form calls them: begin(<counts>) at the start of the
    // kernel, then __gsharp_stack_warp_<kind>(<frame>, <site>, "<place>", <arguments>)

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RunWarp_FullMaskShuffleInTwoBlocks_ReadsTheLanesOfItsOwnBlock()
    {
        const uint blocks = 2;
        const uint threads = 32;
        var output = new int[blocks * threads];

        RunWarp(blocks, threads, "Test.TwoBlockShuffle", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(2);
            int value = ManagedGpuContext.BlockIdxX * 100 + ManagedGpuContext.ThreadIdxX;
            output[tid] = ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)", 0xFFFFFFFFu,
                value, 1);
        });

        for (int b = 0; b < blocks; b++)
        {
            for (int lane = 0; lane < threads; lane++)
            {
                Assert.Equal(b * 100 + (lane ^ 1), output[b * threads + lane]);
            }
        }
    }

    // In block 0 only lane 0 reaches the call (mask 0x1, it reads its own value); in block 1 both lanes reach it with
    // mask 0x3. The pooled OS thread of lane 0 runs both blocks: its count of the call must start again at 0, or it
    // would wait for a second visit of lane 1 that never comes
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RunWarp_OneLaneReachedTheCallInTheBlockBefore_CountsStartAgainInEachBlock()
    {
        var output = new int[4];

        RunWarp(2, 2, "Test.StaleVisits", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(2);
            int block = ManagedGpuContext.BlockIdxX;
            int lane = ManagedGpuContext.ThreadIdxX;
            int value = block * 10 + lane + 1;
            if (block != 0 || lane == 0)
            {
                value += ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)",
                    block == 0 ? 0x1u : 0x3u, value, block == 0 ? 0 : 1);
            }

            output[tid] = value;
        });

        Assert.Equal([2, 2, 23, 23], output);
    }

    // The rendezvous decides from what the lanes published in earlier rounds, never from the order within a round, so
    // the order of UseThreadOrder changes no value
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RunWarp_ShuffledOrderOfEveryRound_GivesTheValuesOfTheIndexOrder()
    {
        const int threads = 64;
        var expected = ReferenceMixedCollectives(threads);
        Assert.Equal(expected, RunMixedCollectives(threads));
        for (var seed = 1; seed <= 8; seed++)
        {
            var random = new Random(seed);
            using (ManagedStackRunner.UseThreadOrder(round =>
                   {
                       var order = Enumerable.Range(0, round.ThreadCount).ToArray();
                       random.Shuffle(order);
                       return order;
                   }))
            {
                Assert.Equal(expected, RunMixedCollectives(threads));
            }
        }
    }

    // Lanes 0 to 2 wait at a shuffle for lane 3, which waits at the block barrier: no thread can run, the launch fails
    // with the stall, and the threads that wait at the shuffle and at the barrier leave through their finally blocks
    // and go back to the pool
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void RunWarp_LaneWaitsAtTheBarrierWhileTheOthersShuffle_UnwindsTheWaitingThreads()
    {
        const uint threads = 4;
        var finallyCounts = new int[threads];
        var afterCounts = new int[threads];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunWarp(1, threads, "Test.WarpStall", _ =>
            {
                int lane = ManagedGpuContext.ThreadIdxX;
                try
                {
                    ManagedGpuContext.__gsharp_stack_warp_begin(2);
                    if (lane == 3)
                    {
                        ManagedGpuContext.__gsharp_stack_block_barrier();
                    }
                    else
                    {
                        ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(7,9)", 0xFu, lane, 1);
                    }

                    afterCounts[lane]++;
                }
                finally
                {
                    finallyCounts[lane]++;
                }
            }));

        Assert.Contains("Test(7,9)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("never reach the same call", ex.Message, StringComparison.Ordinal);
        Assert.Equal([1, 1, 1, 1], finallyCounts);
        Assert.Equal([0, 0, 0, 0], afterCounts);

        var next = new int[threads];
        RunWarp(1, threads, "Test.NextAfterStall", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(2);
            next[tid] = ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)", 0xFu, (int)tid, 1);
        });
        Assert.Equal([1, 0, 3, 2], next);
    }

    // The first failure of a GPU thread wins over the stall that follows it, as on the state machine
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void RunWarp_ThreadFailsThenTheOthersStall_RethrowsTheThreadFailure()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RunWarp(1, 4, "Test.FailThenStall", _ =>
            {
                ManagedGpuContext.__gsharp_stack_warp_begin(2);
                int lane = ManagedGpuContext.ThreadIdxX;
                if (lane == 2)
                {
                    throw new ArgumentException("thread failed first");
                }

                if (lane == 3)
                {
                    ManagedGpuContext.__gsharp_stack_block_barrier();
                }
                else
                {
                    ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)", 0xBu, lane, 1);
                }
            }));

        Assert.Equal("thread failed first", ex.Message);
    }

    // A lane that has finished no longer takes part: the vote of the others goes on without it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RunWarp_LaneFinishedBeforeTheVote_VoteOfTheOthersGoesOn()
    {
        var output = new uint[4];

        RunWarp(1, 4, "Test.VoteAfterExit", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(2);
            if (tid == 1)
            {
                return;
            }

            output[tid] = ManagedGpuContext.__gsharp_stack_warp_ballot_sync(0, 0, "Test(1,1)", 0xFu, true);
        });

        Assert.Equal([0xDu, 0u, 0xDu, 0xDu], output);
    }

    // A launch run without the warp rendezvous (the generated launcher passes it only for a kernel that reaches a
    // collective) never resets the records of its blocks: a collective there is an internal error, not a read of the
    // records an earlier launch left
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StackWarp_LaunchWithoutTheWarpRendezvous_ThrowsAnInternalError()
    {
        RunWarp(1, 2, "Test.WarpBefore", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(2);
            ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)", 0x3u, (int)tid, 1);
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedStackRunner.Run(1, 1, 1, 2, 1, 1, 0, "Test.NoWarpFlag", tid =>
            {
                ManagedGpuContext.__gsharp_stack_warp_begin(2);
                ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)", 0x3u, (int)tid + 100, 1);
            }));

        Assert.Contains("internal error", ex.Message, StringComparison.Ordinal);
    }

    // The count of a collective must lie in the counts that begin cleared: the pooled array is longer, so a frame or
    // site past them, or a collective before begin, would otherwise use a count another kernel left
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(2, 0, 1)]
    [InlineData(4, 2, 1)]
    [InlineData(4, -1, 0)]
    [InlineData(0, 0, 0)]
    public void StackWarp_CountOutsideTheClearedCounts_ThrowsAnInternalError(int size, int frame, int site)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunWarp(1, 2, "Test.CountOutside", tid =>
            {
                if (size > 0)
                {
                    ManagedGpuContext.__gsharp_stack_warp_begin(size);
                }

                ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(frame, site, "Test(1,1)", 0x3u, (int)tid, 1);
            }));

        Assert.Contains("internal error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("outside the", ex.Message, StringComparison.Ordinal);
    }

    // The last count that begin cleared is still inside
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackWarp_LastClearedCount_MeetsAsUsual()
    {
        var output = new int[2];

        RunWarp(1, 2, "Test.LastCount", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(4);
            output[tid] = ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(1, 1, "Test(1,1)", 0x3u, (int)tid,
                1);
        });

        Assert.Equal([1, 0], output);
    }

    // The scheduler takes the block dimensions for the rendezvous and, as the state-machine launcher does, clears the
    // context of the calling thread at the end
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RunWarp_AfterTheLaunch_ClearsTheContextOfTheCaller()
    {
        ManagedGpuContext.IsActive = true;
        try
        {
            RunWarp(1, 2, "Test.WarpCallerClean", _ =>
            {
                ManagedGpuContext.__gsharp_stack_warp_begin(2);
                ManagedGpuContext.__gsharp_stack_warp_syncwarp(0, 0, "Test(1,1)", 0x3u);
            });

            Assert.False(ManagedGpuContext.IsActive);
        }
        finally
        {
            ManagedGpuContext.IsActive = false;
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StackWarpBegin_OutsideALaunch_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() => ManagedGpuContext.__gsharp_stack_warp_begin(2));
    }

    private static void RunWarp(uint blocks, uint threads, string name, Action<long> body) =>
        ManagedStackRunner.Run(blocks, 1, 1, threads, 1, 1, 0, name, reachesWarpCollective: true, body);

    // Lanes 0 to 15 of each warp add the value of their xor neighbor, lanes 16 to 31 take the ballot of their half;
    // the warp meets at Gpu.SyncWarp, then adds the sum of the warp
    private static int[] RunMixedCollectives(int threads)
    {
        var output = new int[threads];
        RunWarp(1, (uint)threads, "Test.MixedCollectives", tid =>
        {
            ManagedGpuContext.__gsharp_stack_warp_begin(5);
            int lane = (int)tid & 31;
            int v = (int)tid * 3 + 1;
            if (lane < 16)
            {
                v += ManagedGpuContext.__gsharp_stack_warp_shuffle_xor_sync(0, 0, "Test(1,1)", 0x0000FFFFu, v, 1);
            }
            else
            {
                v ^= (int)ManagedGpuContext.__gsharp_stack_warp_ballot_sync(0, 1, "Test(2,1)", 0xFFFF0000u,
                    (v & 2) != 0);
            }

            ManagedGpuContext.__gsharp_stack_warp_syncwarp(0, 2, "Test(3,1)");
            v += ManagedGpuContext.__gsharp_stack_warp_reduce_add_sync(0, 3, "Test(4,1)", 0xFFFFFFFFu, v);
            output[tid] = v;
        });
        return output;
    }

    private static int[] ReferenceMixedCollectives(int threads)
    {
        var first = new int[threads];
        for (int tid = 0; tid < threads; tid++)
        {
            int lane = tid & 31;
            int warpBase = tid & ~31;
            int v = tid * 3 + 1;
            if (lane < 16)
            {
                first[tid] = v + ((tid ^ 1) * 3 + 1);
                continue;
            }

            uint ballot = 0;
            for (int other = 16; other < 32; other++)
            {
                if ((((warpBase + other) * 3 + 1) & 2) != 0)
                {
                    ballot |= 1u << other;
                }
            }

            first[tid] = v ^ (int)ballot;
        }

        var output = new int[threads];
        for (int tid = 0; tid < threads; tid++)
        {
            int sum = 0;
            for (int lane = 0; lane < 32; lane++)
            {
                sum += first[(tid & ~31) + lane];
            }

            output[tid] = first[tid] + sum;
        }

        return output;
    }

    private static readonly AsyncLocal<string?> s_callerValue = new();

    // Runs <inner> while a launch on another thread holds a full block of pooled workers, so that the launch of
    // <inner> finds no idle worker and starts new ones
    private static void WhileAFullBlockIsHeld(Action inner)
    {
        const uint fullBlock = 1024;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? holderFailure = null;
        var holder = new Thread(() =>
        {
            try
            {
                ManagedStackRunner.Run(1, 1, 1, fullBlock, 1, 1, 0, "Test.HoldWorkers", _ =>
                {
                    if (ManagedGpuContext.ThreadIdxX == 0)
                    {
                        held.Set();
                        release.Wait();
                    }
                });
            }
            catch (Exception ex)
            {
                holderFailure = ex;
            }
            finally
            {
                held.Set();
            }
        });
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(60)));
        try
        {
            inner();
        }
        finally
        {
            release.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(60)));
        }

        Assert.Null(holderFailure);
    }

    private static int[] RunReverseOn(ApartmentState apartment)
    {
        var output = new int[4];
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var scratch = new int[4];
                ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.Apartment", _ =>
                {
                    int local = ManagedGpuContext.ThreadIdxX;
                    scratch[local] = local;
                    ManagedGpuContext.__gsharp_stack_block_barrier();
                    output[local] = scratch[3 - local];
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        Assert.Null(failure);
        return output;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFirstFailure() => throw new InvalidOperationException("first failure");
}
