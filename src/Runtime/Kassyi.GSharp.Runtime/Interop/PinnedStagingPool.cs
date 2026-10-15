namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// Process-wide pool of <see cref="PinnedStagingBuffer"/> instances for non-blocking device-to-host copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Allocating page-locked memory is cheap enough (well under a millisecond, independent of device load),
/// but <c>cuMemFreeHost</c> waits for all work on every stream of the device, even unrelated ones, to finish. The pool
/// therefore never frees on the caller's path: it keeps up to <see cref="MaxIdleBuffers"/> idle
/// buffers and hands any excess to a single reclaimer, so at most one thread-pool thread is ever blocked by a free.
/// </para>
/// <para>
/// <b>Thread safety:</b> lock-free. <see cref="Return"/> may run on the CUDA driver's host-function thread and therefore
/// performs only managed operations.
/// </para>
/// </remarks>
internal static class PinnedStagingPool
{
    /// <summary>The number of idle buffers kept for reuse (16 MiB of page-locked memory).</summary>
    public const int MaxIdleBuffers = 16;

    private static readonly PinnedStagingBuffer?[] s_idle = new PinnedStagingBuffer?[MaxIdleBuffers];
    private static readonly ConcurrentQueue<PinnedStagingBuffer> s_excess = new();
    private static readonly Reclaimer s_reclaimer = new();
    private static int s_reclaimerScheduled;
    private static int s_allocatedCount;

    /// <summary>Gets the number of buffers allocated by the pool and not yet freed, whether idle or rented.</summary>
    public static int AllocatedCount => Volatile.Read(ref s_allocatedCount);

    /// <summary>Gets the number of buffers currently idle in the pool.</summary>
    public static int IdleCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < s_idle.Length; i++)
            {
                if (Volatile.Read(ref s_idle[i]) is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Rents a buffer and takes the caller's reference on it, allocating a new buffer when none is idle.
    /// </summary>
    /// <returns>A buffer that the caller must release with <see cref="PinnedStagingBuffer.Release"/>.</returns>
    /// <exception cref="CudaException">A new buffer was needed and could not be allocated.</exception>
    public static PinnedStagingBuffer Rent()
    {
        for (var i = 0; i < s_idle.Length; i++)
        {
            var candidate = Interlocked.Exchange(ref s_idle[i], null);
            if (candidate is null)
            {
                continue;
            }

            candidate.AcquireForCaller();
            return candidate;
        }

        var created = PinnedStagingBuffer.Allocate();
        Interlocked.Increment(ref s_allocatedCount);
        created.AcquireForCaller();
        return created;
    }

    /// <summary>
    /// Takes back a buffer whose references have all been released.
    /// </summary>
    /// <param name="buffer">The buffer; no copy into it may be in flight.</param>
    public static void Return(PinnedStagingBuffer buffer)
    {
        for (var i = 0; i < s_idle.Length; i++)
        {
            if (Interlocked.CompareExchange(ref s_idle[i], buffer, null) is null)
            {
                return;
            }
        }

        s_excess.Enqueue(buffer);
        ScheduleReclaimer();
    }

    private static void ScheduleReclaimer()
    {
        if (Interlocked.CompareExchange(ref s_reclaimerScheduled, 1, 0) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(s_reclaimer, preferLocal: false);
        }
    }

    /// <summary>
    /// Frees excess buffers one at a time on a single thread-pool thread.
    /// </summary>
    private sealed class Reclaimer : IThreadPoolWorkItem
    {
        public void Execute()
        {
            while (s_excess.TryDequeue(out var buffer))
            {
                try
                {
                    buffer.Dispose();
                }
                catch (CudaException ex)
                {
                    // The memory stays allocated but is no longer tracked; a broken context cannot reuse it either way.
                    Logger.Warning(nameof(PinnedStagingPool), $"Failed to free a pinned staging buffer: {ex.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref s_allocatedCount);
                }
            }

            Volatile.Write(ref s_reclaimerScheduled, 0);

            // A buffer enqueued after the loop drained the queue but before the flag was cleared found the reclaimer
            // still scheduled and did not reschedule it.
            if (!s_excess.IsEmpty)
            {
                ScheduleReclaimer();
            }
        }
    }
}
