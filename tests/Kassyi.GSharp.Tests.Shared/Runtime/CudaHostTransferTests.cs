using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Validates non-blocking device-to-host transfers through the pinned staging pool (<see cref="CudaHostTransfer"/>).
/// </summary>
/// <remarks>
/// The pool is process-wide, so its counters are only compared within a test. Running in
/// <see cref="TestCollections.DeviceExclusive"/> guarantees that no other test rents staging buffers meanwhile.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class CudaHostTransferTests
{
    private const int WordsPerChunk = PinnedStagingBuffer.Size / sizeof(uint);
    private const uint Sentinel = 0xDEADBEEF;

    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableTheory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(WordsPerChunk - 1)]
    [InlineData(WordsPerChunk)]
    [InlineData(WordsPerChunk + 1)]
    [InlineData((3 * WordsPerChunk) + 5)]
    public async Task CopyToHostAsync_RoundTripsData_AcrossChunkBoundaries(int count)
    {
        CudaTestEnvironment.SkipIfNoDevice();

        using var source = DeviceData.Create(count);
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            var destination = new uint[count];

            await CudaHostTransfer.CopyToHostAsync(source.Pointer, destination.AsMemory(), stream.Handle)
                .AsTask().WaitAsync(CompletionTimeout);

            AssertPattern(destination);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public async Task CopyToHostAsync_ElementSizeNotDividingChunk_RoundTripsData()
    {
        CudaTestEnvironment.SkipIfNoDevice();

        // 1 MiB is not a multiple of 12 bytes, so every full chunk ends short of the staging buffer size and the source
        // address must advance by the bytes actually copied.
        var elementCount = (2 * (PinnedStagingBuffer.Size / Unsafe.SizeOf<WordTriple>())) + 1;
        using var source = DeviceData.Create(elementCount * 3);
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            var destination = new WordTriple[elementCount];

            await CudaHostTransfer.CopyToHostAsync(source.Pointer, destination.AsMemory(), stream.Handle)
                .AsTask().WaitAsync(CompletionTimeout);

            AssertPattern(MemoryMarshal.Cast<WordTriple, uint>(destination));
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void CopyToHostAsync_ElementLargerThanStagingBuffer_ThrowsNotSupported()
    {
        // Rejected before the device or fallback check, so the outcome does not depend on the execution mode.
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = CudaHostTransfer.CopyToHostAsync(IntPtr.Zero, Memory<OversizedElement>.Empty, IntPtr.Zero);
        });
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CopyToHostAsync_ReturnsBeforeQueuedWorkCompletes()
    {
        CudaTestEnvironment.SkipIfNoDevice();

        using var source = DeviceData.Create(1024);
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            var destination = new uint[1024];
            using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(stream.Handle, GateHoldTime));

            var pending = CudaHostTransfer.CopyToHostAsync(source.Pointer, destination.AsMemory(), stream.Handle)
                .AsTask();
            Assert.False(pending.IsCompleted, "The copy must be enqueued, not waited for, while the stream is busy.");

            await pending.WaitAsync(CompletionTimeout);
            Assert.True(gate.IsReleased, "The copy completed before the work queued ahead of it.");
            AssertPattern(destination);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public async Task CopyToHostAsync_SequentialCopies_ReuseStagingBuffers()
    {
        CudaTestEnvironment.SkipIfNoDevice();

        using var source = DeviceData.Create(4096);
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            var destination = new uint[4096];
            await CudaHostTransfer.CopyToHostAsync(source.Pointer, destination.AsMemory(), stream.Handle);
            var allocatedBefore = PinnedStagingPool.AllocatedCount;

            for (var i = 0; i < 100; i++)
            {
                Array.Clear(destination);
                await CudaHostTransfer.CopyToHostAsync(source.Pointer, destination.AsMemory(), stream.Handle);
            }

            Assert.Equal(allocatedBefore, PinnedStagingPool.AllocatedCount);
            AssertPattern(destination);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void CopyToHostAsync_EmptyDestination_CompletesWithoutDeviceAccess()
    {
        var pending = CudaHostTransfer.CopyToHostAsync(IntPtr.Zero, Memory<uint>.Empty, IntPtr.Zero);

        Assert.True(pending.IsCompletedSuccessfully);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public async Task CopyToHostAsync_PreCanceledToken_DoesNotTouchDestination()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var destination = new[] { Sentinel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CudaHostTransfer.CopyToHostAsync(IntPtr.Zero, destination.AsMemory(), IntPtr.Zero, cts.Token));

        Assert.Equal(Sentinel, destination[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public async Task CopyToHostAsync_CanceledInFlight_LeavesDestinationUntouched_AndHoldsStagingUntilDmaCompletes()
    {
        CudaTestEnvironment.SkipIfNoDevice();

        using var source = DeviceData.Create(1024);
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            // The warm-up copy leaves at least one idle staging buffer, which the canceled copy then rents.
            await CudaHostTransfer.CopyToHostAsync(source.Pointer, new uint[1].AsMemory(), stream.Handle);
            var idleBefore = PinnedStagingPool.IdleCount;

            var destination = new uint[1024];
            Array.Fill(destination, Sentinel);
            using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(stream.Handle, GateHoldTime));
            using var cts = new CancellationTokenSource();

            var pending = CudaHostTransfer
                .CopyToHostAsync(source.Pointer, destination.AsMemory(), stream.Handle, cts.Token).AsTask();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(CompletionTimeout));
            Assert.False(gate.IsReleased, "Cancellation must detach the awaiter before the DMA runs.");
            Assert.Equal(idleBefore - 1, PinnedStagingPool.IdleCount);

            // Let the DMA into the staging buffer happen; only then may the buffer return to the pool.
            gate.Dispose();
            CudaTestEnvironment.InContext(stream.Synchronize);
            await WaitUntilAsync(() => PinnedStagingPool.IdleCount == idleBefore);

            Assert.All(destination, word => Assert.Equal(Sentinel, word));
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void CopyToHostAsync_OnCapturingStream_ThrowsWithoutRentingStaging()
    {
        CudaTestEnvironment.SkipIfNoDevice();

        using var source = DeviceData.Create(16);
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        var idleBefore = PinnedStagingPool.IdleCount;
        var allocatedBefore = PinnedStagingPool.AllocatedCount;

        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream.Handle, 1));
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = CudaHostTransfer.CopyToHostAsync(source.Pointer, new uint[16].AsMemory(), stream.Handle);
            });
        }
        finally
        {
            CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream.Handle, out var graph));
            if (graph != IntPtr.Zero)
            {
                CudaDriverApi.cuGraphDestroy(graph);
            }
        }

        Assert.Equal(idleBefore, PinnedStagingPool.IdleCount);
        Assert.Equal(allocatedBefore, PinnedStagingPool.AllocatedCount);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void CopyToHostAsync_InCpuFallbackMode_CopiesFromHostPointer()
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        var host = Marshal.AllocHGlobal(16 * sizeof(uint));
        try
        {
            var words = new Span<uint>((void*)host, 16);
            for (var i = 0; i < words.Length; i++)
            {
                words[i] = Pattern(i);
            }

            var destination = new uint[16];
            var pending = CudaHostTransfer.CopyToHostAsync(host, destination.AsMemory(), IntPtr.Zero);

            Assert.True(pending.IsCompletedSuccessfully);
            AssertPattern(destination);
        }
        finally
        {
            Marshal.FreeHGlobal(host);
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    private static uint Pattern(int index) => unchecked(((uint)index * 2654435761u) + 17u);

    private static void AssertPattern(ReadOnlySpan<uint> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            if (destination[i] != Pattern(i))
            {
                Assert.Fail($"Word {i} is 0x{destination[i]:X8}, expected 0x{Pattern(i):X8}.");
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + CompletionTimeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not reached in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>A fieldless 12-byte element; the tests read it back as three words.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 3 * sizeof(uint))]
    private struct WordTriple
    {
    }

    /// <summary>An element that cannot fit in a single staging buffer.</summary>
    [StructLayout(LayoutKind.Sequential, Size = PinnedStagingBuffer.Size + 1)]
    private struct OversizedElement
    {
    }

    /// <summary>Device memory filled with <see cref="Pattern"/>.</summary>
    private sealed unsafe class DeviceData : IDisposable
    {
        private DeviceData(IntPtr pointer) => Pointer = pointer;

        public IntPtr Pointer { get; private set; }

        public static DeviceData Create(int count)
        {
            var host = new uint[count];
            for (var i = 0; i < host.Length; i++)
            {
                host[i] = Pattern(i);
            }

            return CudaTestEnvironment.InContext(() =>
            {
                var bytes = (IntPtr)((long)count * sizeof(uint));
                CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var pointer, bytes));
                fixed (uint* p = host)
                {
                    CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(pointer, (IntPtr)p, bytes));
                }

                // A synchronous copy from pageable memory may return once the data is staged, before the DMA lands, and
                // the non-blocking test streams are not ordered after the legacy stream. Wait until the data is resident.
                CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
                return new DeviceData(pointer);
            });
        }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero)
            {
                return;
            }

            var pointer = Pointer;
            Pointer = IntPtr.Zero;
            CudaTestEnvironment.InContext(() => CudaDriverApi.cuMemFree_v2(pointer));
        }
    }
}
