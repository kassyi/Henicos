#nullable enable
using System;
using System.Globalization;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[Collection(TestCollections.DeviceExclusive)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class ManagedStackCooperativeTests
{
    private const string MemoryLimitEnvVar = "GSHARP_TEST_STACK_MEMORY_LIMIT_BYTES";
    private const string ThreadLimitEnvVar = "GSHARP_TEST_STACK_THREAD_LIMIT";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CooperativeLaunch_FailureWhileOthersWaitAtGridSync_ReturnsEveryWorker()
    {
        const uint blocks = 2;
        const uint threadsPerBlock = 4;

        // Warm up the pool with 8 threads so CreatedThreadCount stays stable across launches.
        ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.CoopWarmup",
            reachesWarpCollective: false, cooperative: true, _ =>
            {
                ManagedGpuContext.__gsharp_stack_grid_barrier();
            });

        int idleBefore = ManagedStackRunner.IdleThreadCount;
        int createdBefore = ManagedStackRunner.CreatedThreadCount;

        // Thread 0 of block 0 throws before reaching SyncGrid; all other threads of block 0 wait at SyncGrid.
        // This also kills Mutant 3 (RepresentativeFinishedAtGridSync check ignoring first is null).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.CoopFailFirst",
                reachesWarpCollective: false, cooperative: true, _ =>
                {
                    if (ManagedGpuContext.BlockIdxX == 0 && ManagedGpuContext.ThreadIdxX == 0)
                    {
                        throw new InvalidOperationException("thread 0 failed before grid sync");
                    }

                    ManagedGpuContext.__gsharp_stack_grid_barrier();
                }));

        Assert.Equal("thread 0 failed before grid sync", ex.Message);
        Assert.Equal(idleBefore, ManagedStackRunner.IdleThreadCount);

        var reached = new int[blocks * threadsPerBlock];
        ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.CoopAfterFailure",
            reachesWarpCollective: false, cooperative: true, tid =>
            {
                ManagedGpuContext.__gsharp_stack_grid_barrier();
                reached[tid] = 1;
            });

        Assert.All(reached, value => Assert.Equal(1, value));
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CooperativeLaunch_OverMemoryLimit_ThrowsBeforeStartingThreads()
    {
        const uint blocks = 2;
        const uint threadsPerBlock = 16; // 32 threads * 64 KiB = 2 MiB; limit 1 MiB -> half is 512 KiB
        int createdBefore = ManagedStackRunner.CreatedThreadCount;
        bool bodyRan = false;
        string? previous = Environment.GetEnvironmentVariable(MemoryLimitEnvVar);

        using (ManagedStackThreadPool.Instance.BorrowIdleThreadsForTest())
        {
            Environment.SetEnvironmentVariable(MemoryLimitEnvVar, (1024 * 1024).ToString(CultureInfo.InvariantCulture));
            try
            {
                var ex = Assert.Throws<InsufficientMemoryException>(() =>
                    ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.OverMemory",
                        reachesWarpCollective: false, cooperative: true, _ => bodyRan = true));

                Assert.Contains("Test.OverMemory", ex.Message, StringComparison.Ordinal);
                Assert.Contains("32 GPU threads", ex.Message, StringComparison.Ordinal);
                Assert.Contains("half of the free memory is the limit", ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(MemoryLimitEnvVar, previous);
            }
        }

        Assert.False(bodyRan);
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CooperativeLaunch_ThreadStartFails_ThrowsResourceError()
    {
        const uint blocks = 2;
        const uint threadsPerBlock = 4; // 8 threads total
        const int failAtIndex = 3;
        var pool = ManagedStackThreadPool.Instance;

        using (pool.BorrowIdleThreadsForTest())
        {
            int idleBefore = pool.IdleThreadCount;
            int createdBefore = pool.CreatedThreadCount;
            bool bodyRan = false;
            ManagedStackThreadPool.BeforeThreadStartForTest = index =>
            {
                if (index == failAtIndex)
                {
                    throw new OutOfMemoryException("simulated thread start failure");
                }
            };
            try
            {
                var ex = Assert.Throws<InsufficientMemoryException>(() =>
                    ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.StartFails",
                        reachesWarpCollective: false, cooperative: true, _ => bodyRan = true));

                Assert.Contains("could start only 3 of 8", ex.Message, StringComparison.Ordinal);
                Assert.IsType<OutOfMemoryException>(ex.InnerException);
            }
            finally
            {
                ManagedStackThreadPool.BeforeThreadStartForTest = null;
            }

            Assert.False(bodyRan);
            Assert.Equal(createdBefore + failAtIndex, pool.CreatedThreadCount);
            Assert.Equal(idleBefore + failAtIndex, pool.IdleThreadCount);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CooperativeLaunch_OverTheThreadLimit_ThrowsBeforeStartingThreads()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "OS thread limit pre-check applies only on Linux.");

        const uint blocks = 2;
        const uint threadsPerBlock = 8; // 16 threads; override limit 10 -> 80% is 8 (< 16 + processThreads)
        int createdBefore = ManagedStackRunner.CreatedThreadCount;
        bool bodyRan = false;
        string? previous = Environment.GetEnvironmentVariable(ThreadLimitEnvVar);

        using (ManagedStackThreadPool.Instance.BorrowIdleThreadsForTest())
        {
            Environment.SetEnvironmentVariable(ThreadLimitEnvVar, "10");
            try
            {
                var ex = Assert.Throws<InsufficientMemoryException>(() =>
                    ManagedStackRunner.Run(blocks, 1, 1, threadsPerBlock, 1, 1, 0, "Test.OverThreadLimit",
                        reachesWarpCollective: false, cooperative: true, _ => bodyRan = true));

                Assert.Contains("Test.OverThreadLimit", ex.Message, StringComparison.Ordinal);
                Assert.Contains("needs 16 OS threads", ex.Message, StringComparison.Ordinal);
                Assert.Contains(ThreadLimitEnvVar, ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ThreadLimitEnvVar, previous);
            }
        }

        Assert.False(bodyRan);
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }
}
