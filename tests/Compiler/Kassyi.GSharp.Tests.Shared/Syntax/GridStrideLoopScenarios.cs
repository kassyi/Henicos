using System;
using System.Collections.Generic;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="GridStrideLoopKernels"/> (#712). The expected values follow from the
/// definition of <c>Gpu.GridStrideLoop</c>: every index of <c>[0, count)</c> is visited exactly once across the launch,
/// whatever its shape, and the count argument is evaluated once per thread, as a C# argument is.
/// </summary>
internal static class GridStrideLoopScenarios
{
    /// <summary>A launch shape and a count.</summary>
    public readonly record struct Launch(uint GridX, uint GridY, uint GridZ, uint BlockX, uint BlockY, uint BlockZ, int Count)
    {
        public override string ToString() => $"grid {GridX}x{GridY}x{GridZ}, block {BlockX}x{BlockY}x{BlockZ}, count {Count}";
    }

    public static TheoryData<int> LaunchIndices()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < s_launches.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static readonly Launch[] s_launches =
    [
        // One axis: fewer and more indices than threads
        new(2, 1, 1, 32, 1, 1, 200),
        new(2, 1, 1, 32, 1, 1, 40),
        // Two block axes (#712: each index was visited twice)
        new(1, 1, 1, 32, 2, 1, 32),
        // Each other axis on its own, and all of them
        new(1, 1, 1, 8, 1, 2, 1000),
        new(1, 3, 1, 16, 1, 1, 1000),
        new(1, 1, 2, 16, 1, 1, 1000),
        new(3, 2, 2, 4, 3, 2, 1000),
        new(3, 2, 2, 4, 3, 2, 5),
        // No iteration, a negative count, one index
        new(1, 1, 1, 64, 1, 1, 0),
        new(1, 1, 1, 64, 1, 1, -5),
        new(1, 1, 1, 64, 1, 1, 1),
    ];

    public static Launch Get(int index) => s_launches[index];

    public static void CountVisits(Action<Action> run, Launch launch, bool withContext)
    {
        // Room past the count, so that a visit outside [0, count) shows as well
        var visits = new int[Math.Max(launch.Count, 0) + 64];

        run(() =>
        {
            if (withContext)
            {
                GridStrideLoopKernels.LaunchCountVisitsWithContextKernel(visits, launch.Count, launch.GridX, launch.GridY,
                    launch.GridZ, launch.BlockX, launch.BlockY, launch.BlockZ);
            }
            else
            {
                GridStrideLoopKernels.LaunchCountVisitsKernel(visits, launch.Count, launch.GridX, launch.GridY, launch.GridZ,
                    launch.BlockX, launch.BlockY, launch.BlockZ);
            }
        });

        AssertVisitedOnce(visits, launch.Count, launch.ToString());
    }

    public static void CountReadOnce(Action<Action> run)
    {
        int[] length = [4];
        var output = new int[8];

        run(() => GridStrideLoopKernels.LaunchCountReadOnceKernel(length, output, blockDimX: 1));

        Assert.Equal([1, 1, 1, 1, 0, 0, 0, 0], output);
        Assert.Equal(0, length[0]);
    }

    public static void CountCallOnce(Action<Action> run)
    {
        const int Count = 100;
        const uint Threads = 2 * 32;
        var calls = new int[1];
        var visits = new int[Count + 64];

        run(() => GridStrideLoopKernels.LaunchCountCallOnceKernel(calls, visits, Count, gridDimX: 2, blockDimX: 32));

        Assert.Equal((int)Threads, calls[0]);
        AssertVisitedOnce(visits, Count, "count from a call");
    }

    /// <summary>
    /// A launch of more than 2^31 threads (GPU only): the rank and the step of the threads no longer fit in an
    /// <c>int</c>, and every index is still visited once.
    /// </summary>
    public static void CountVisitsOnAHugeLaunch(Action<Action> run, uint gridX, int count)
    {
        var visits = new int[count + 64];

        run(() => GridStrideLoopKernels.LaunchCountVisitsKernel(visits, count, gridDimX: gridX, blockDimX: 1024));

        AssertVisitedOnce(visits, count, $"grid {gridX}, block 1024, count {count}");
    }

    private static void AssertVisitedOnce(int[] visits, int count, string label)
    {
        var wrong = new List<string>();
        for (int i = 0; i < visits.Length; i++)
        {
            int expected = i < count ? 1 : 0;
            if (visits[i] != expected)
            {
                wrong.Add($"[{i}]={visits[i]}");
            }
        }

        Assert.True(wrong.Count == 0, $"{label}: {wrong.Count} wrong visit counts: {string.Join(", ", wrong.GetRange(0, Math.Min(wrong.Count, 20)))}");
    }
}
