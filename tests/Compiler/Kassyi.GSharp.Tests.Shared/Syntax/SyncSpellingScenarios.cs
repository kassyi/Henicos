using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="SyncSpellingKernels"/>: a barrier or warp shuffle spelled through a
/// <c>using</c> alias or <c>using static</c> must order the threads as <c>Gpu.SyncThreads()</c> / <c>Warp.ShuffleDownSync</c>
/// do (#503). The native and managed test classes share these scenarios and differ only in how a launch runs.
/// </summary>
internal static class SyncSpellingScenarios
{
    public static void AliasBarrier(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchAliasBarrierRotateKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 2, 3, 4, 1 }, output);
    }

    public static void UsingStaticBarrier(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchUsingStaticBarrierRotateKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 2, 3, 4, 1 }, output);
    }

    public static void AliasShuffle(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.WarpSize];

        run(() => SyncSpellingKernels.LaunchAliasShuffleDownKernel(output, blockDimX: SyncSpellingKernels.WarpSize));

        Assert.Equal(ExpectedShuffleDown(), output);
    }

    public static void UsingStaticShuffle(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.WarpSize];

        run(() => SyncSpellingKernels.LaunchUsingStaticShuffleDownKernel(output, blockDimX: SyncSpellingKernels.WarpSize));

        Assert.Equal(ExpectedShuffleDown(), output);
    }

    public static void QualifiedBarrier(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchQualifiedBarrierRotateKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 2, 3, 4, 1 }, output);
    }
    public static void UserSyncThreads(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchUserSyncThreadsKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 10, 11, 12, 13 }, output);
    }
    public static void LookalikeGpuSyncThreads(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchLookalikeGpuSyncThreadsKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 20, 21, 22, 23 }, output);
    }
    public static void UsingStaticThreadIndex(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchUsingStaticThreadIndexKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 0, 3, 6, 9 }, output);
    }
    public static void OwnPopCount(Action<Action> run)
    {
        var output = new int[SyncSpellingKernels.BlockSize];

        run(() => SyncSpellingKernels.LaunchOwnPopCountKernel(output, blockDimX: SyncSpellingKernels.BlockSize));

        Assert.Equal(new[] { 100, 101, 102, 103 }, output);
    }
    public static void UsingStaticFastExp(Action<Action> run)
    {
        float[] data = [0f, 1f, 2f, -1f];
        var expected = Array.ConvertAll(data, MathF.Exp);

        run(() => SyncSpellingKernels.LaunchUsingStaticFastExpKernel(data, blockDimX: (uint)data.Length));

        // __expf on CUDA is within a few ulps of exp
        for (int i = 0; i < data.Length; i++)
        {
            Assert.True(MathF.Abs(data[i] - expected[i]) <= 1e-5f * expected[i], $"exp at {i}: {data[i]} vs {expected[i]}");
        }
    }

    public static void UsingStaticAtomic(Action<Action> run)
    {
        var counter = new int[1];

        run(() => SyncSpellingKernels.LaunchUsingStaticAtomicKernel(counter, blockDimX: SyncSpellingKernels.WarpSize));

        Assert.Equal(SyncSpellingKernels.WarpSize, counter[0]);
    }

    // Lane i holds i + 1 and reads lane i + 1; the last lane has no lane above it and keeps its own value
    private static int[] ExpectedShuffleDown()
    {
        var expected = new int[SyncSpellingKernels.WarpSize];
        for (int lane = 0; lane < expected.Length; lane++)
        {
            expected[lane] = lane + 1 < expected.Length ? lane + 2 : lane + 1;
        }

        return expected;
    }
}
