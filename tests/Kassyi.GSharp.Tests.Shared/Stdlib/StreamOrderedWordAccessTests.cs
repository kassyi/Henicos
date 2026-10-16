using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// Validates that single-word device access issued with a stream is ordered on that stream instead of on the legacy
/// null stream, which waits for every blocking stream of the context.
/// </summary>
/// <remarks>
/// Streams created with flags 0 (<c>CU_STREAM_DEFAULT</c>) are blocking, exactly like <see cref="GpuWorkspace.Stream"/>.
/// Another blocking stream is held busy with a <see cref="StreamGate"/>: an access that returns while the gate is
/// still closed provably did not wait for it. <see cref="CudaStream"/> is not used because it is non-blocking and
/// therefore never synchronizes with the null stream in the first place.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class StreamOrderedWordAccessTests
{
    private const int NumWords = 16;
    private const uint Marker = 0xC0FFEE42;

    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void WriteWord_ThenReadWord_OnSameStream_RoundTripsAtOffset()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        var stream = CreateBlockingStream();
        var pointer = handler.AllocateMemory(NumWords);
        try
        {
            handler.ClearMemoryAsync(pointer, NumWords, stream);
            handler.WriteWord(pointer, 3, Marker, stream);
            handler.WriteWord(pointer, NumWords - 1, 7u, stream);

            Assert.Equal(Marker, handler.ReadWord(pointer, 3, stream));
            Assert.Equal(7u, handler.ReadWord(pointer, NumWords - 1, stream));
            Assert.Equal(0u, handler.ReadWord(pointer, 4, stream));
        }
        finally
        {
            handler.FreeMemory(pointer);
            DestroyStream(stream);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ReadWord_WithStream_ObservesEarlierWorkOnThatStream()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        var stream = CreateBlockingStream();
        var pointer = handler.AllocateMemory(NumWords);
        try
        {
            // Both operations are only enqueued; the read must still be ordered after them on the same stream.
            handler.WriteWord(pointer, 0, Marker, stream);
            handler.ClearMemoryAsync(pointer, NumWords, stream);
            handler.WriteWord(pointer, 1, Marker + 1, stream);

            Assert.Equal(0u, handler.ReadWord(pointer, 0, stream));
            Assert.Equal(Marker + 1, handler.ReadWord(pointer, 1, stream));
        }
        finally
        {
            handler.FreeMemory(pointer);
            DestroyStream(stream);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ReadWord_WithStream_DoesNotWaitForOtherBlockingStreams()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        var busy = CreateBlockingStream();
        var own = CreateBlockingStream();
        var pointer = handler.AllocateMemory(NumWords);
        try
        {
            using var gate = StreamGate.Enqueue(busy, GateHoldTime);
            _ = handler.ReadWord(pointer, 0, own);

            Assert.False(gate.IsReleased, "A stream-ordered read must not wait for unrelated blocking streams.");
        }
        finally
        {
            handler.FreeMemory(pointer);
            DestroyStream(busy);
            DestroyStream(own);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void WriteWord_WithStream_DoesNotWaitForOtherBlockingStreams()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        var busy = CreateBlockingStream();
        var own = CreateBlockingStream();
        var pointer = handler.AllocateMemory(NumWords);
        try
        {
            using var gate = StreamGate.Enqueue(busy, GateHoldTime);
            handler.WriteWord(pointer, 0, Marker, own);

            Assert.False(gate.IsReleased, "A stream-ordered write must not wait for unrelated blocking streams.");
        }
        finally
        {
            handler.FreeMemory(pointer);
            DestroyStream(busy);
            DestroyStream(own);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void WordAccess_WithoutStream_KeepsLegacyNullStreamSemantics()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        var busy = CreateBlockingStream();
        var pointer = handler.AllocateMemory(NumWords);
        try
        {
            using var gate = StreamGate.Enqueue(busy, GateHoldTime);
            handler.WriteWord(pointer, 0, Marker);
            var afterWrite = gate.IsReleased;

            Assert.True(afterWrite, "Without a stream the write stays on the null stream and waits for blocking streams.");
            Assert.Equal(Marker, handler.ReadWord(pointer));
        }
        finally
        {
            handler.FreeMemory(pointer);
            DestroyStream(busy);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigUIntSignAndParityQueries_DoNotWaitForAnotherWorkspaceStream()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var holder = new GpuWorkspace(NumWords);
        using var workspace = new GpuWorkspace(NumWords);
        using var value = new GpuBigUInt(workspace, NumWords);
        value.Clear();
        workspace.Provider.WriteWord(value.DevicePointer, 0, 2u, workspace.Stream);
        workspace.Provider.WriteWord(value.DevicePointer, NumWords - 1, 0x80000000u, workspace.Stream);

        using var gate = StreamGate.Enqueue(holder.Stream, GateHoldTime);
        var isEven = value.GetIsEven();
        var isNegative = value.GetIsNegative();
        value.SetIsNegative(false);
        var afterClear = value.GetIsNegative();

        Assert.False(gate.IsReleased, "Queries must be ordered on the workspace stream, not on the null stream.");
        Assert.True(isEven);
        Assert.True(isNegative);
        Assert.False(afterClear);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void CpuProvider_WordAccess_AcceptsAndIgnoresStream()
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            var provider = CSharpCpuBigIntProvider.Instance;
            var pointer = provider.AllocateMemory(NumWords);
            try
            {
                provider.WriteWord(pointer, 2, Marker, stream: 0x1234);

                Assert.Equal(Marker, provider.ReadWord(pointer, 2, stream: 0x1234));
            }
            finally
            {
                provider.FreeMemory(pointer);
            }
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    private static IntPtr CreateBlockingStream()
    {
        // CU_STREAM_DEFAULT (0): blocking, so it synchronizes with the legacy null stream like GpuWorkspace.Stream does.
        CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out var stream, 0));
        return stream;
    }

    private static void DestroyStream(IntPtr stream) => CudaDriverApi.cuStreamDestroy(stream);
}
