using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// Validates the asynchronous members of <see cref="IGpuMemoryProvider"/> and the synchronous readback contract.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class AsyncMemoryProviderTests
{
    private const int Words = 40;

    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CopyDeviceToHostAsync_MatchesSynchronousCopy()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);
        var expected = number.ToHostArray();
        var actual = new uint[Words];

        await workspace.Provider.CopyDeviceToHostAsync(actual.AsMemory(), number.DevicePointer, Words, workspace.Stream)
            .AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CopyDeviceToHostAsync_IsolatedContextWorkspace_ReturnsData()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot, useIsolatedContext: true);
        GpuBigUInt number;
        uint[] expected;
        using (workspace.ActivateContext())
        {
            number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);
            expected = number.ToHostArray();
        }

        try
        {
            // The staging buffers live in the global context; the copy runs on the isolated context's stream.
            var actual = new uint[Words];
            await workspace.Provider
                .CopyDeviceToHostAsync(actual.AsMemory(), number.DevicePointer, Words, workspace.Stream)
                .AsTask().WaitAsync(CompletionTimeout);

            Assert.Equal(expected, actual);
        }
        finally
        {
            using (workspace.ActivateContext())
            {
                number.Dispose();
            }
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task SynchronizeStreamAsync_CompletesOnlyAfterQueuedWork()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));

        var pending = workspace.Provider.SynchronizeStreamAsync(workspace.Stream).AsTask();
        Assert.False(pending.IsCompleted, "The wait must not block the caller while the stream is busy.");

        await pending.WaitAsync(CompletionTimeout);
        Assert.True(gate.IsReleased);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void SynchronizeStreamAsync_NullStream_CompletesImmediately()
    {
        // Mirrors SynchronizeStream, which treats the null stream as "nothing to wait for".
        var pending = new AotMemoryHandler().SynchronizeStreamAsync(IntPtr.Zero);

        Assert.True(pending.IsCompletedSuccessfully);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void CopyDeviceToHostAsync_NumWordsExceedsDestination_Throws()
    {
        IGpuMemoryProvider[] providers = [new AotMemoryHandler(), CSharpCpuBigIntProvider.Instance];

        foreach (var provider in providers)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = provider.CopyDeviceToHostAsync(new uint[2].AsMemory(), IntPtr.Zero, 3, IntPtr.Zero);
            });
        }
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CpuProvider_AsyncMembers_CompleteSynchronously()
    {
        var provider = CSharpCpuBigIntProvider.Instance;
        var pointer = provider.AllocateMemory(Words);
        try
        {
            for (var i = 0; i < Words; i++)
            {
                provider.WriteWord(pointer, i, (uint)(i + 1));
            }

            var actual = new uint[Words];
            var copy = provider.CopyDeviceToHostAsync(actual.AsMemory(), pointer, Words, stream: 0x1234);
            var wait = provider.SynchronizeStreamAsync(0x1234);

            Assert.True(copy.IsCompletedSuccessfully);
            Assert.True(wait.IsCompletedSuccessfully);
            Assert.Equal(1u, actual[0]);
            Assert.Equal((uint)Words, actual[^1]);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            Assert.True(provider.SynchronizeStreamAsync(0x1234, cts.Token).IsCanceled);
            Assert.True(provider.CopyDeviceToHostAsync(actual.AsMemory(), pointer, Words, 0x1234, cts.Token).IsCanceled);
        }
        finally
        {
            provider.FreeMemory(pointer);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void CopyDeviceToHost_PageLockedDestination_StillReturnsCompletedData()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);
        var expected = number.ToHostArray();

        // A page-locked destination turns the async copy inside CopyDeviceToHost into a truly asynchronous one, so only
        // an explicit wait keeps the synchronous contract. The pinned object heap keeps the address stable.
        var destination = GC.AllocateArray<uint>(Words, pinned: true);
        IntPtr address;
        fixed (uint* p = destination)
        {
            address = (IntPtr)p;
        }

        var registered = CudaTestEnvironment.InContext(() =>
            CudaDriverApi.cuMemHostRegister(address, Words * sizeof(uint), 0));
        Skip.If(registered != CUresult.CudaSuccess, $"Host memory registration is unavailable: {registered}");
        try
        {
            using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));

            workspace.Provider.CopyDeviceToHost(destination, number.DevicePointer, Words, workspace.Stream);
            var returnedAfterQueuedWork = gate.IsReleased;

            Assert.True(returnedAfterQueuedWork, "The synchronous copy returned before the work queued ahead of it.");
            Assert.Equal(expected, destination);
        }
        finally
        {
            CudaTestEnvironment.InContext(() => CudaDriverApi.cuMemHostUnregister(address));
        }
    }

    private static BigInteger CreateValue()
    {
        var bytes = new byte[Words * sizeof(uint)];
        new Random(7).NextBytes(bytes);
        bytes[^1] |= 0x40;
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }
}
