using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Runtime.Aot;

#pragma warning disable SA1402 // ManagedStackRunner and its round record live together as the stack scheduler API
/// <summary>
/// Runs a kernel's stack form once per GPU thread on pooled OS threads, stepping one thread at a time in the same
/// deterministic block and round order as the managed state-machine launcher. For generated code only.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ManagedStackRunner
{
    [ThreadStatic]
    private static ManagedStackLaunch? t_launch;

    // Counted per launching thread, so that a test reads only its own launches while other tests launch in parallel
    [ThreadStatic]
    private static long t_launchCount;

    // The launches of the calling thread that have started and not yet ended: more than one only while a wait of the
    // scheduler runs another launch
    [ThreadStatic]
    private static int t_activeLaunches;

    private static readonly AsyncLocal<Func<ManagedStackRound, int[]>?> s_threadOrder = new();

    /// <summary>The launches that the calling thread has run.</summary>
    internal static long LaunchCountOnThisThread => t_launchCount;

    internal static int CreatedThreadCount => ManagedStackThreadPool.Instance.CreatedThreadCount;

    internal static int IdleThreadCount => ManagedStackThreadPool.Instance.IdleThreadCount;

    internal static int AbsorbedInterruptCount => ManagedStackThreadPool.Instance.AbsorbedInterruptCount;

    // The interruptions that the scheduler of the calling thread dropped while a failed wait waited for its worker
    internal static int DiscardedInterruptsOnThisThread => t_launch?.Scheduler.DiscardedInterrupts ?? 0;

    // The block state of the calling thread, by identity only: a launch run inside a wait of another launch of the
    // thread gives the outer launch its block state back when it ends
    internal static object BlockStateOfThisThread => ManagedGpuContext.ShareBlockState();

    /// <summary>
    /// Runs <paramref name="thread"/> once per GPU thread of the launch, stepping blocks in index order and threads
    /// within each block in index order (or the order returned by <see cref="UseThreadOrder"/>) up to each block
    /// barrier. For generated code only.
    /// </summary>
    /// <param name="gridDimX">The number of blocks in x.</param>
    /// <param name="gridDimY">The number of blocks in y.</param>
    /// <param name="gridDimZ">The number of blocks in z.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="sharedMemBytes">The dynamic shared memory size of each block, in bytes.</param>
    /// <param name="kernelName">The kernel name as <c>Type.Method</c>, for diagnostics.</param>
    /// <param name="thread">The call of the kernel, given the linear index of the GPU thread.</param>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void Run(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes, string kernelName, Action<long> thread) =>
        Run(gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, kernelName,
            reachesWarpCollective: false, thread);

    /// <summary>
    /// Runs <paramref name="thread"/> once per GPU thread of the launch, as
    /// <see cref="Run(uint, uint, uint, uint, uint, uint, uint, string, Action{long})"/> does; the threads of a kernel
    /// that reaches a warp collective also meet there, in the rounds of the managed state-machine launcher. For
    /// generated code only.
    /// </summary>
    /// <param name="gridDimX">The number of blocks in x.</param>
    /// <param name="gridDimY">The number of blocks in y.</param>
    /// <param name="gridDimZ">The number of blocks in z.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="sharedMemBytes">The dynamic shared memory size of each block, in bytes.</param>
    /// <param name="kernelName">The kernel name as <c>Type.Method</c>, for diagnostics.</param>
    /// <param name="reachesWarpCollective">Whether the kernel reaches a warp collective.</param>
    /// <param name="thread">The call of the kernel, given the linear index of the GPU thread.</param>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void Run(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes, string kernelName, bool reachesWarpCollective, Action<long> thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var geometry = new LaunchGeometry(gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ);
        LaunchLimits.GetManagedContextCount(geometry.TotalBlocks, blockDimX, blockDimY, blockDimZ, kernelName);

        // [WHY] A wait of the scheduler can run another launch on the same thread (a SynchronizationContext that asks
        // for wait notification, as a UI dispatcher does, or a message pumped during the wait of an STA thread). That
        // launch gets its own launch record, worker array and block state, so that it leaves the waiting launch's
        // untouched
        var nested = t_activeLaunches > 0;
        var launch = nested ? new ManagedStackLaunch() : (t_launch ??= new ManagedStackLaunch());
        var outerState = nested ? ManagedGpuContext.SwapBlockState(new ManagedBlockState()) : null;
        launch.ReachesWarpCollective = reachesWarpCollective;
        t_activeLaunches++;
        try
        {
            RunLaunch(geometry, sharedMemBytes, launch, nested, thread);
        }
        finally
        {
            t_activeLaunches--;
            if (nested)
            {
                ManagedGpuContext.SwapBlockState(outerState);
            }
        }
    }

    private static void RunLaunch(in LaunchGeometry geometry, uint sharedMemBytes, ManagedStackLaunch launch,
        bool nested, Action<long> thread)
    {
        var threadsPerBlock = checked((int)geometry.ThreadsPerBlock);
        launch.State = ManagedGpuContext.ShareBlockState();
        launch.Cancelled = false;
        ManagedGpuContext.InitializeGrid(checked((int)geometry.TotalBlocks), sharedMemBytes, blocksRunInPhases: false);
        t_launchCount++;

        ExceptionDispatchInfo? first = null;
        ManagedStackThread[]? workers = null;
        var orderProvider = s_threadOrder.Value;
        var seen = orderProvider is not null ? new bool[threadsPerBlock] : null;
        try
        {
            workers = ManagedStackThreadPool.Instance.Rent(threadsPerBlock, nested);
            var schedule = new BlockSchedule(geometry, launch, workers, threadsPerBlock, orderProvider, seen);
            for (long block = 0; block < geometry.TotalBlocks; block++)
            {
                schedule.AssignBlock(block, thread);
                schedule.RunBlock(block, ref first);
            }
        }
        catch (Exception) when (first is not null)
        {
            first.Throw();
        }
        finally
        {
            try
            {
                Unwind(launch, workers, threadsPerBlock);
            }
            finally
            {
                // The scheduler took the block dimensions for the warp rendezvous; it leaves no GPU thread context
                // behind, as the state-machine launcher clears its own at the end of a launch
                if (launch.ReachesWarpCollective)
                {
                    ManagedGpuContext.ClearThreadContext();
                }
            }
        }

        first?.Throw();
    }

    // [WHY] Each step runs even when the one before it throws: a failed cancellation must still return the workers and
    // end the grid, or the caller's thread keeps the grid and the pool loses the threads
    private static void Unwind(ManagedStackLaunch launch, ManagedStackThread[]? workers, int count)
    {
        try
        {
            if (workers is not null)
            {
                CancelWaiting(launch, workers, count);
            }
        }
        finally
        {
            try
            {
                if (workers is not null)
                {
                    ManagedStackThreadPool.Instance.Return(workers, count);
                }
            }
            finally
            {
                launch.Cancelled = false;
                launch.State = null;
                ManagedGpuContext.EndGrid();
            }
        }
    }

    internal static IDisposable UseThreadOrder(Func<ManagedStackRound, int[]> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var previous = s_threadOrder.Value;
        s_threadOrder.Value = order;
        return new ThreadOrderScope(previous);
    }

    private static int[] GetOrder(Func<ManagedStackRound, int[]> orderProvider, in ManagedStackRound round, bool[] seen)
    {
        var ids = orderProvider(round) ??
            throw new InvalidOperationException($"The thread order returned null for {round.ThreadCount} threads.");
        var count = round.ThreadCount;
        if (ids.Length != count)
        {
            throw new InvalidOperationException($"The thread order gave {ids.Length} ids for {count} threads.");
        }

        Array.Clear(seen, 0, count);
        foreach (var id in ids)
        {
            if ((uint)id >= (uint)count || seen[id])
            {
                throw new InvalidOperationException($"The thread order is not a permutation of 0..{count - 1}: {id}.");
            }

            seen[id] = true;
        }

        return ids;
    }

    private static void CancelWaiting(ManagedStackLaunch launch, ManagedStackThread[] workers, int count)
    {
        launch.Cancelled = true;
        ExceptionDispatchInfo? failure = null;
        for (var t = 0; t < count; t++)
        {
            var w = workers[t];
            // A worker that this launch never gave a block to (the launch failed before) has nothing to unwind, and
            // waking it would leave it without a launch
            if (w.Launch is null)
            {
                continue;
            }

            while (!w.Done)
            {
                try
                {
                    ManagedBaton.HandOver(w.Go, launch.Scheduler);
                }
                catch (Exception ex)
                {
                    // [WHY] HandOver throws only after the worker answered, so the two threads are in step: the
                    // unwinding goes on until every worker has left the kernel and can return to the pool, and the
                    // first failure is thrown after it
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            w.Failure = null;
        }

        failure?.Throw();
    }

    // The rounds of the blocks of one launch: the scheduler gives the baton to each thread of the block in turn (in
    // index order, or the order of UseThreadOrder) until each has finished, reached the block barrier or reached a warp
    // collective, and runs the next round while a thread can go on; when none can, it releases the barrier
    private readonly struct BlockSchedule(
        LaunchGeometry geometry,
        ManagedStackLaunch launch,
        ManagedStackThread[] workers,
        int threadsPerBlock,
        Func<ManagedStackRound, int[]>? orderProvider,
        bool[]? seen)
    {
        // Gives each worker its GPU thread of <block>
        public void AssignBlock(long block, Action<long> thread)
        {
            var blockBase = block * threadsPerBlock;
            for (var t = 0; t < threadsPerBlock; t++)
            {
                var w = workers[t];
                var id = blockBase + t;
                w.Launch = launch;
                w.Body = thread;
                w.LinearThreadId = id;
                w.Context = geometry.ContextOf(id);
                w.AtBlockBarrier = false;
                w.AtWarp = false;
                w.WarpVisitCount = 0;
                w.Done = false;
                w.Failure = null;
            }

            if (launch.ReachesWarpCollective)
            {
                BindWarpScheduler();
                ManagedGpuContext.ResetWarpExchange();
            }
        }

        // Runs the rounds of <block>; the first failure of a GPU thread goes to <first>, and the block goes on
        public void RunBlock(long block, ref ExceptionDispatchInfo? first)
        {
            var round = 0;
            bool anyActive;
            do
            {
                round++;
                anyActive = RunRound(block, round, ref first);
            }
            while (anyActive);
        }

        // One round, as the managed state-machine launcher runs it (ManagedFallbackEmitter.WriteManagedBarrierRounds):
        // a thread that waits at a warp collective counts as one that can go on, and the threads at the block barrier
        // pass it only when no thread can run; whether another round follows. A kernel without warp collectives brings
        // every thread to the barrier or its end in one step, so the barrier is released after every round, as before
        private bool RunRound(long block, int round, ref ExceptionDispatchInfo? first)
        {
            var order = GetRoundOrder(block, round);
            var warp = launch.ReachesWarpCollective;
            var anyActive = false;
            var stepped = false;
            for (var k = 0; k < threadsPerBlock; k++)
            {
                var t = order is null ? k : order[k];
                var w = workers[t];
                if (w.Done || w.AtBlockBarrier)
                {
                    continue;
                }

                if (warp && !ManagedGpuContext.WarpCanRun(t, round))
                {
                    anyActive = true;
                    continue;
                }

                stepped = true;
                Step(w, ref first);
                if (warp)
                {
                    // [WHY] A launch that a wait of this step ran on the scheduler's thread left its own dimensions there
                    BindWarpScheduler();
                    if (w.Done)
                    {
                        ManagedGpuContext.MarkWarpLaneExited(t);
                    }
                }

                anyActive |= !w.Done && !w.AtBlockBarrier;
            }

            // [WHY] A round in which no thread ran while some wait at a warp collective means that they wait for lanes
            // that never reach the same call (#1468)
            if (warp && anyActive && !stepped)
            {
                throw ManagedGpuContext.WarpStall(threadsPerBlock);
            }

            if (!anyActive)
            {
                for (var t = 0; t < threadsPerBlock; t++)
                {
                    if (workers[t].AtBlockBarrier)
                    {
                        workers[t].AtBlockBarrier = false;
                        anyActive = true;
                    }
                }
            }

            return anyActive;
        }

        // The warp rendezvous reads the block dimensions of the calling thread (WarpCanRun, WarpStall): the scheduler
        // takes those of the launch, as the state-machine launcher does
        private void BindWarpScheduler()
        {
            var dimensions = geometry.Dimensions(threadsPerBlock);
            ManagedGpuContext.BlockDimX = (int)dimensions.BlockDimX;
            ManagedGpuContext.BlockDimY = (int)dimensions.BlockDimY;
            ManagedGpuContext.BlockDimZ = (int)dimensions.BlockDimZ;
        }

        private int[]? GetRoundOrder(long block, int round)
        {
            if (orderProvider is null || seen is null)
            {
                return null;
            }

            var dimensions = geometry.Dimensions(threadsPerBlock);
            var roundInfo = new ManagedStackRound(
                dimensions.GridDimX, dimensions.GridDimY, dimensions.GridDimZ,
                dimensions.BlockDimX, dimensions.BlockDimY, dimensions.BlockDimZ,
                checked((int)block), round, threadsPerBlock);
            return GetOrder(orderProvider, roundInfo, seen);
        }

        // Runs <w> until it finishes, reaches the block barrier or reaches a warp collective
        private void Step(ManagedStackThread w, ref ExceptionDispatchInfo? first)
        {
            ManagedBaton.HandOver(w.Go, launch.Scheduler);
            if (w.Failure is { } failure)
            {
                first ??= failure;
                w.Failure = null;
            }

            if (!w.Done && !w.AtBlockBarrier && !w.AtWarp)
            {
                throw new InvalidOperationException(
                    "G#: a stack thread returned to the scheduler without finishing or reaching a block " +
                    "barrier or a warp collective");
            }
        }
    }

    private sealed class ThreadOrderScope(Func<ManagedStackRound, int[]>? previous) : IDisposable
    {
        public void Dispose() => s_threadOrder.Value = previous;
    }
}

internal readonly record struct ManagedStackRound(
    uint GridDimX,
    uint GridDimY,
    uint GridDimZ,
    uint BlockDimX,
    uint BlockDimY,
    uint BlockDimZ,
    int BlockIndex,
    int Round,
    int ThreadCount
);
#pragma warning restore SA1402
