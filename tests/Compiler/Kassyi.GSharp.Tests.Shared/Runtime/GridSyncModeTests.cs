using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <c>Gpu.SyncGrid(GridSyncMode)</c>: the CUDA build calls the Deterministic barrier for the default and for
/// <see cref="Kassyi.GSharp.Core.GridSyncMode.Deterministic"/>, and the Fast barrier for every constant spelling of
/// <see cref="Kassyi.GSharp.Core.GridSyncMode.Fast"/>; both barriers order the writes of every block, on every path;
/// and with Fast, a thread that fails before the barrier still makes the launch throw without leaving a block waiting.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public partial class GridSyncModeTests
{
    private const int Untouched = -7;
    private const int BlockSize = 128;
    private const int Blocks = 4;

    private const string DeterministicBarrier = "__gsharp_grid_sync();";
    private const string FastBarrier = "__gsharp_grid_sync_fast();";

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void GridSyncMode_DeterministicMode_EmitsOnlyDeterministicBarrier()
    {
        var (source, failures) = TranspileTestRunner.TranspileMethod(typeof(GridSyncModeKernels),
            nameof(GridSyncModeKernels.DeterministicOnlyKernel), isDevice: false, dialect: new GSharpCudaDialect());

        Assert.Empty(failures);
        Assert.Equal(1, Occurrences(source, DeterministicBarrier));
        Assert.Equal(0, Occurrences(source, FastBarrier));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void GridSyncMode_FastMode_EmitsOnlyFastBarrier()
    {
        var (source, failures) = TranspileTestRunner.TranspileMethod(typeof(GridSyncModeKernels),
            nameof(GridSyncModeKernels.FastOnlyKernel), isDevice: false, dialect: new GSharpCudaDialect());

        Assert.Empty(failures);
        Assert.Equal(1, Occurrences(source, FastBarrier));
        Assert.Equal(0, Occurrences(source, DeterministicBarrier));
    }

    // The default and Deterministic give the Deterministic barrier; the member, a const and a cast of Fast give the
    // Fast one, in the order written, and no barrier passes the mode as an argument
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void GridSyncMode_EveryConstantSpelling_PicksBarrierOfItsMode()
    {
        var (source, failures) = TranspileTestRunner.TranspileMethod(typeof(GridSyncModeKernels),
            nameof(GridSyncModeKernels.EveryModeSpellingKernel), isDevice: false, dialect: new GSharpCudaDialect());

        Assert.Empty(failures);
        var barriers = GridBarriers(source);
        Assert.Equal([DeterministicBarrier, DeterministicBarrier, FastBarrier, FastBarrier, FastBarrier], barriers);
    }

    // A Fast barrier written as a statement of a Gpu.SyncGrid(body) stays Fast, and the body inserts no barrier next
    // to it; the barriers it inserts are Deterministic
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void GridSyncMode_FastInsideBody_KeepsModeAndInsertedBarriersAreDeterministic()
    {
        var (source, failures) = TranspileTestRunner.TranspileMethod(typeof(GridSyncModeKernels),
            nameof(GridSyncModeKernels.FastInsideBodyKernel), isDevice: false, dialect: new GSharpCudaDialect());

        Assert.Empty(failures);
        var barriers = GridBarriers(source);
        Assert.Equal([DeterministicBarrier, FastBarrier, DeterministicBarrier], barriers);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void GridSyncMode_FastInsideBody_OrdersWritesOfEveryBlock()
    {
        RequireDevice();
        int threads = Blocks * BlockSize;
        var values = new int[threads];
        var result = new int[threads];

        GridSyncModeKernels.LaunchFastInsideBodyKernel(values, result, gridDimX: Blocks, blockDimX: BlockSize);

        for (int i = 0; i < threads; i++)
        {
            Assert.Equal(3 * ((i + BlockSize) % threads + 1), result[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void GridSyncMode_EveryModeSpelling_OrdersWritesOfEveryBlock()
    {
        RequireDevice();
        int threads = Blocks * BlockSize;
        var values = new int[threads];
        var result = new int[threads];

        GridSyncModeKernels.LaunchEveryModeSpellingKernel(values, result, gridDimX: Blocks, blockDimX: BlockSize);

        // Each read after a barrier sees the value the next block wrote before it: 1, 2 and 3 times (next + 1)
        for (int i = 0; i < threads; i++)
        {
            Assert.Equal(6 * ((i + BlockSize) % threads + 1), result[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void FastBarrier_ThousandRounds_ExecutesExactly()
    {
        RequireDevice();
        const int Rounds = 1000;
        int threads = Blocks * BlockSize;
        var buffer = new int[2 * threads];

        GridSyncModeKernels.LaunchFastRoundsKernel(buffer, Rounds, gridDimX: Blocks, blockDimX: BlockSize);

        // An even number of rounds ends in the first half; every value there is the number of rounds
        for (int i = 0; i < threads; i++)
        {
            Assert.Equal(Rounds, buffer[i]);
        }
    }

    private static void RequireDevice()
    {
        if (AssemblyDialect.Current.IsCuda)
        {
            CudaCapability.EnsureGpuAvailable();
        }
    }

    // The grid barrier calls of the generated source, in order, each line without its indentation
    private static string[] GridBarriers(string source)
    {
        var barriers = new List<string>();
        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("__gsharp_grid_sync", StringComparison.Ordinal))
            {
                barriers.Add(trimmed.ToString());
            }
        }

        return [.. barriers];
    }

    private static int Occurrences(string source, string text)
    {
        int count = 0;
        for (int at = source.IndexOf(text, StringComparison.Ordinal); at >= 0;
             at = source.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
