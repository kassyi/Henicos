using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>
/// Carries the failures of on-device corrections to the host without adding a synchronization: two device words per
/// <see cref="GpuWorkspace"/> (a failure count and the code of the last failure), read together with the reads that GpuMath
/// already makes on the workspace stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>Writers:</b> the final corrections of <c>ReduceZeroSync</c> (<c>BarrettCorrectionKernel</c>), <c>ReciprocalMega</c> and
/// <c>InverseSqrtMega</c> add 1 to the count with an atomic add and store their code
/// (<see cref="GpuBigIntDivRemKernels.ReduceZeroSyncCorrectionFailed"/> and the two after it) in the second word when they
/// cannot confirm their result within their step limit; that result is then wrong. The count only grows: nothing resets
/// it. The host marks the status pending (<see cref="MarkPending"/>) right after launching such a kernel or a graph.
/// </para>
/// <para>
/// <b>Readers:</b> <see cref="GSharpKernelProvider.CopyDeviceToHost"/>, <see cref="GSharpKernelProvider.ReadWord"/>,
/// <see cref="GSharpKernelProvider.SynchronizeStream"/> and their asynchronous forms, the kernel-result read of
/// <see cref="KernelBasicMathHandler"/> (bit lengths, comparisons) and the chunk wait of the ModPow mega kernel. When the
/// status is pending and the stream is not being captured, <see cref="BeginRead()"/> enqueues an asynchronous copy of
/// the two words into a page-locked slot; the reader calls <see cref="EndRead"/> after its own wait. A count above the
/// largest count already reported is thrown as <see cref="InvalidOperationException"/> once (the largest count is updated
/// with a compare-and-exchange); for the asynchronous forms the returned <see cref="ValueTask"/> completes with it. Reads
/// while nothing is pending cost nothing. A read that ends without its wait (an exception or a cancellation) marks the
/// status pending again, so the next read copies the words.
/// </para>
/// <para>
/// <b>Several threads:</b> a workspace may be used from several threads at once. Because the count only grows and copies
/// on one stream land in stream order, the slot never goes back to a smaller count, so concurrent readers, and kernels
/// launched between a copy and its check, cannot hide a failure: every increase of the count is reported by exactly one
/// read, which may cover several failures.
/// </para>
/// <para>
/// <b>Not reported at the next read:</b> reads on the legacy null stream (<c>stream</c> = <see cref="IntPtr.Zero"/>); waits
/// that bypass GpuMath (<c>cuStreamSynchronize</c> or <c>cuMemcpyDtoH</c> called directly, for example by a caller that
/// synchronizes the stream itself and then reads <see cref="GpuBigUInt.DevicePointer"/>), which leave the status pending for
/// the next GpuMath read; and CPU fallback mode, which has no device words. A graph captured on one workspace and replayed
/// on another stream (or with <c>cuGraphLaunch</c> directly) counts into the capturing workspace's words without marking
/// it pending, so its failure is reported only after that workspace launches a correction or a graph again and then
/// reads.
/// </para>
/// <para>
/// <b>Slots:</b> the page-locked slots come from pages allocated once per process and never freed, because
/// <c>cuMemFreeHost</c> waits for every stream of the device. A disposed workspace returns its slot once no read that
/// copies into it is in flight. A slot that a read abandoned before its wait (its copy may still land) is never reused;
/// that happens only after a failed or cancelled read, so the number of such slots does not grow in normal use.
/// </para>
/// </remarks>
internal sealed class GpuCorrectionStatus : IDisposable
{
    /// <summary>The number of device words: the failure count and the code of the last failure.</summary>
    public const int DeviceWords = 2;

    private static readonly ConcurrentDictionary<IntPtr, GpuCorrectionStatus> s_byStream = new();
    private static int s_discardedSlots;

    private readonly IGpuExecutionProvider _provider;
    private readonly IntPtr _stream;
    private readonly IntPtr _context;
    private readonly IntPtr _slot;
    private readonly AsyncCheck _asyncCheck;
    private int _pending;
    private int _reported;
    private int _readsInFlight;
    private int _slotAbandoned;
    private int _slotReleased;
    private int _disposed;

    /// <summary>
    /// Allocates and clears the device words on <paramref name="stream"/> and registers them for the stream.
    /// </summary>
    /// <param name="provider">The provider that allocates and frees the device words.</param>
    /// <param name="stream">The workspace stream; it must not be the null stream.</param>
    /// <param name="context">The workspace's isolated context, or <see cref="IntPtr.Zero"/> for the shared one.</param>
    public GpuCorrectionStatus(IGpuExecutionProvider provider, IntPtr stream, IntPtr context)
    {
        _provider = provider;
        _stream = stream;
        _context = context;
        _asyncCheck = new AsyncCheck(this);
        _slot = PinnedSlots.Rent();
        DevicePointer = provider.AllocateMemory(DeviceWords);
        provider.ClearMemoryAsync(DevicePointer, DeviceWords, stream);
        s_byStream[stream] = this;
    }

    /// <summary>Gets the device address of the status words passed to the correction kernels.</summary>
    public IntPtr DevicePointer { get; }

    /// <summary>Gets the largest failure count reported so far (for tests).</summary>
    internal int ReportedFailures => Volatile.Read(ref _reported);

    /// <summary>Gets a value indicating whether the slot has been returned or discarded (for tests).</summary>
    internal bool SlotReleased => Volatile.Read(ref _slotReleased) == 1;

    /// <summary>Gets the number of slots never returned because a read abandoned them (for tests).</summary>
    internal static int DiscardedSlots => Volatile.Read(ref s_discardedSlots);

    /// <summary>Finds the status registered for a workspace stream.</summary>
    /// <param name="stream">The stream of a read or a launch.</param>
    /// <returns>The status, or <see langword="null"/> for the null stream or a stream without a workspace.</returns>
    public static GpuCorrectionStatus? Find(IntPtr stream) =>
        stream != IntPtr.Zero && s_byStream.TryGetValue(stream, out var status) ? status : null;

    /// <summary>Begins a check on the status of a workspace stream; see <see cref="BeginRead()"/>.</summary>
    /// <param name="stream">The stream of a read.</param>
    /// <returns>The status to check after the read's wait, or <see langword="null"/> when there is nothing to check.</returns>
    public static GpuCorrectionStatus? BeginRead(IntPtr stream) => Find(stream)?.BeginRead();

    /// <summary>
    /// Records that a correction kernel or a graph was launched on the stream; the next read checks the words. Called
    /// after the launch.
    /// </summary>
    public void MarkPending() => Volatile.Write(ref _pending, 1);

    /// <summary>
    /// When a correction may have run since the previous check, enqueues the asynchronous copy of the device words into
    /// the page-locked slot. The caller must wait for the stream (its own read does) and then call <see cref="EndRead"/>,
    /// or call <see cref="AbandonRead"/> when it ends without that wait.
    /// </summary>
    /// <returns>This status when there is a copy to check, or <see langword="null"/> when there is nothing to check.</returns>
    public GpuCorrectionStatus? BeginRead()
    {
        if (Volatile.Read(ref _pending) == 0)
        {
            return null;
        }

        using var scope = new GpuKernelLoader.CudaContextScope(_context);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamIsCapturing(_stream, out var captureStatus));
        if (captureStatus != CUstreamCaptureStatus.None)
        {
            // A copy enqueued now would become a node of the caller's graph; the request stays for a later read.
            return null;
        }

        // Cleared before the copy: a kernel launched after this point marks the status again, and one launched before
        // it is ahead of the copy on the stream.
        Volatile.Write(ref _pending, 0);
        Interlocked.Increment(ref _readsInFlight);
        try
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyDtoHAsync_v2(_slot, DevicePointer, DeviceWords * sizeof(uint), _stream));
        }
        catch
        {
            // Nothing was enqueued, so the request goes back for the next read.
            MarkPending();
            FinishRead();
            throw;
        }

        return this;
    }

    /// <summary>
    /// Records that a read begun with <see cref="BeginRead()"/> ended without waiting for the stream. The next read copies
    /// the words again; the slot is not reused, because this copy may still land in it.
    /// </summary>
    public void AbandonRead()
    {
        Volatile.Write(ref _slotAbandoned, 1);
        MarkPending();
        FinishRead();
    }

    /// <summary>
    /// Ends a read begun with <see cref="BeginRead()"/> after its wait, and throws when the copied count is above the
    /// largest count reported so far.
    /// </summary>
    /// <exception cref="InvalidOperationException">A correction did not converge since the previous report.</exception>
    public unsafe void EndRead()
    {
        var count = (int)Volatile.Read(ref *(uint*)_slot);
        var code = Volatile.Read(ref *((uint*)_slot + 1));
        FinishRead();

        while (true)
        {
            var reported = Volatile.Read(ref _reported);
            if (count <= reported)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _reported, count, reported) == reported)
            {
                break;
            }
        }

        throw new InvalidOperationException(
            $"The on-device correction of {Describe(code)} did not converge within its step limit, so the result of that call is wrong " +
            $"({count} failure(s) on this workspace so far). The failure is reported at the first read of the workspace stream after the call.");
    }

    /// <summary>
    /// Returns a task that completes when <paramref name="read"/> does, with the failure of a correction if the copied
    /// count is new. One read at a time uses a reusable source, so awaiting repeatedly does not allocate.
    /// </summary>
    /// <param name="read">The asynchronous read that waits for the copy enqueued by <see cref="BeginRead()"/>.</param>
    /// <returns>The read followed by the check.</returns>
    public ValueTask EndReadAfter(ValueTask read) =>
        _asyncCheck.TryStart(read, out var checkedRead) ? checkedRead : EndReadAfterSlow(read, this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        s_byStream.TryRemove(new KeyValuePair<IntPtr, GpuCorrectionStatus>(_stream, this));
        _provider.FreeMemory(DevicePointer, _stream);
        TryReleaseSlot();
    }

    // Used while another asynchronous read of the same workspace holds the reusable source.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask EndReadAfterSlow(ValueTask read, GpuCorrectionStatus status)
    {
        try
        {
            await read.ConfigureAwait(false);
        }
        catch
        {
            status.AbandonRead();
            throw;
        }

        status.EndRead();
    }

    private static string Describe(uint code) => code switch
    {
        GpuBigIntDivRemKernels.ReduceZeroSyncCorrectionFailed => "GpuBigIntDivRem.ReduceZeroSync (BarrettCorrectionKernel)",
        GpuBigIntDivRemKernels.ReciprocalCorrectionFailed => "GpuBigIntDivRem.Reciprocal (ReciprocalMega)",
        GpuBigIntDivRemKernels.InverseSqrtCorrectionFailed => "GpuBigIntDivRem.InverseSqrt (InverseSqrtMega)",
        _ => $"an unknown operation (status {code})"
    };

    private void FinishRead()
    {
        if (Interlocked.Decrement(ref _readsInFlight) == 0)
        {
            TryReleaseSlot();
        }
    }

    // Returns the slot once the workspace is disposed and no copy into it is in flight; an abandoned slot is discarded.
    private void TryReleaseSlot()
    {
        if (Volatile.Read(ref _disposed) == 0 || Volatile.Read(ref _readsInFlight) != 0 ||
            Interlocked.Exchange(ref _slotReleased, 1) == 1)
        {
            return;
        }

        if (Volatile.Read(ref _slotAbandoned) == 0)
        {
            PinnedSlots.Return(_slot);
        }
        else
        {
            Interlocked.Increment(ref s_discardedSlots);
        }
    }

    /// <summary>
    /// A reusable <see cref="IValueTaskSource"/> that awaits one read and then checks the slot. It is held from
    /// <see cref="TryStart"/> until the caller consumes the result of the completed operation.
    /// </summary>
    private sealed class AsyncCheck(GpuCorrectionStatus owner) : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _core;
        private ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter _read;
        private Action? _onReadCompleted;
        private int _busy;

        public bool TryStart(ValueTask read, out ValueTask checkedRead)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                checkedRead = default;
                return false;
            }

            _core.Reset();
            _read = read.ConfigureAwait(false).GetAwaiter();
            checkedRead = new ValueTask(this, _core.Version);
            if (_read.IsCompleted)
            {
                OnReadCompleted();
            }
            else
            {
                _read.UnsafeOnCompleted(_onReadCompleted ??= OnReadCompleted);
            }

            return true;
        }

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token,
            ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

        public void GetResult(short token)
        {
            // Only the consumer of the current, completed operation frees the source; a stale or early call does not.
            var release = token == _core.Version && _core.GetStatus(token) != ValueTaskSourceStatus.Pending;
            try
            {
                _core.GetResult(token);
            }
            finally
            {
                if (release)
                {
                    Volatile.Write(ref _busy, 0);
                }
            }
        }

        private void OnReadCompleted()
        {
            var read = _read;
            _read = default;
            try
            {
                read.GetResult();
            }
            catch (Exception ex)
            {
                owner.AbandonRead();
                _core.SetException(ex);
                return;
            }

            try
            {
                owner.EndRead();
                _core.SetResult(true);
            }
            catch (InvalidOperationException ex)
            {
                _core.SetException(ex);
            }
        }
    }

    /// <summary>Page-locked 8-byte slots carved from pages that live for the whole process.</summary>
    private static unsafe class PinnedSlots
    {
        private const int SlotsPerPage = 512;
        private const int SlotBytes = DeviceWords * sizeof(uint);

        // CU_MEMHOSTALLOC_PORTABLE: page-locked for every context, so copies on isolated-context streams stay asynchronous.
        private const uint PortableFlag = 0x01;

        private static readonly Lock s_lock = new();
        private static readonly Stack<IntPtr> s_free = new();

        public static IntPtr Rent()
        {
            lock (s_lock)
            {
                if (s_free.Count == 0)
                {
                    AllocatePage();
                }

                var slot = s_free.Pop();
                new Span<uint>((void*)slot, DeviceWords).Clear();
                return slot;
            }
        }

        public static void Return(IntPtr slot)
        {
            lock (s_lock)
            {
                s_free.Push(slot);
            }
        }

        private static void AllocatePage()
        {
            using var scope = new GpuKernelLoader.CudaContextScope(GpuKernelLoader.GlobalContext);
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemHostAlloc(out var page, SlotsPerPage * SlotBytes, PortableFlag));
            for (var i = SlotsPerPage - 1; i >= 0; i--)
            {
                s_free.Push(IntPtr.Add(page, i * SlotBytes));
            }
        }
    }
}
