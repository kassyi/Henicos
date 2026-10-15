using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// Validates the asynchronous readback APIs of <see cref="GpuBigUInt"/> and the asynchronous
/// <see cref="FermatAlgorithm"/> entry points against their synchronous counterparts.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class AsyncReadbackTests
{
    private const int Words = 40;
    private const int FermatExponent = 10;

    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task ToHostArrayAsync_MatchesToHostArray()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);

        var actual = await number.ToHostArrayAsync().AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(number.ToHostArray(), actual);
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToBigIntegerAsync_MatchesToBigInteger(bool isNegative)
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);

        var actual = await number.ToBigIntegerAsync(isNegative).AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(number.ToBigInteger(isNegative), actual);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task ToBigIntegerAsync_ReturnsBeforeQueuedWorkCompletes()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);
        var expected = number.ToBigInteger();
        using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));

        var pending = number.ToBigIntegerAsync().AsTask();
        Assert.False(pending.IsCompleted, "The readback must not block while the stream is busy.");

        Assert.Equal(expected, await pending.WaitAsync(CompletionTimeout));
        Assert.True(gate.IsReleased);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ToBigIntegerAsync_CanceledInFlight_Throws_AndStreamStaysUsable()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);
        var expected = number.ToBigInteger();
        using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));
        using var cts = new CancellationTokenSource();

        var pending = number.ToBigIntegerAsync(cancellationToken: cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(CompletionTimeout));
        Assert.Equal(expected, await number.ToBigIntegerAsync().AsTask().WaitAsync(CompletionTimeout));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ToHostArrayAsync_PreCanceledToken_Throws()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await number.ToHostArrayAsync(cts.Token));
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CpuFallback_ToBigIntegerAsync_CompletesSynchronously()
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(64);
            using var number = GpuBigUInt.FromBigInteger(workspace, CreateValue(), Words);

            var pending = number.ToBigIntegerAsync();

            Assert.True(pending.IsCompletedSuccessfully);
            Assert.Equal(CreateValue(), await pending);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Fermat_RunFermatPrpAsync_MatchesRunFermatPrp()
    {
        CudaCapability.EnsureGpuAvailable();

        using var syncRunner = new FermatAlgorithm(FermatExponent);
        using var asyncRunner = new FermatAlgorithm(FermatExponent);
        var (expected, _) = CudaTestEnvironment.InContext(syncRunner.RunFermatPrp);

        var (actual, _) = await asyncRunner.RunFermatPrpAsync().AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Fermat_RunBenchmarkChunkAsync_ThenGetStateAsync_MatchesSynchronousCounterparts()
    {
        CudaCapability.EnsureGpuAvailable();

        using var syncRunner = new FermatAlgorithm(FermatExponent);
        using var asyncRunner = new FermatAlgorithm(FermatExponent);
        var expected = CudaTestEnvironment.InContext(() =>
        {
            syncRunner.RunBenchmarkChunk(4);
            return syncRunner.GetState();
        });

        await asyncRunner.RunBenchmarkChunkAsync(4).AsTask().WaitAsync(CompletionTimeout);
        var actual = await asyncRunner.GetStateAsync().AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task Fermat_PreCanceledToken_ThrowsWithoutRunning()
    {
        CudaCapability.EnsureGpuAvailable();

        using var runner = new FermatAlgorithm(FermatExponent);
        var initial = await runner.GetStateAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.RunFermatPrpAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.RunBenchmarkChunkAsync(4, cts.Token));

        Assert.Equal(initial, await runner.GetStateAsync());
    }

    private static BigInteger CreateValue()
    {
        var bytes = new byte[Words * sizeof(uint)];
        new Random(11).NextBytes(bytes);
        bytes[^1] |= 0x40;
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }
}
