#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kassyi.GSharp.Tests.Shared.Differential;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Cpu.Benchmarks;

[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class BarrierModelBenchmarkTests
{
    private const string BenchOutEnvVar = "GSHARP_BARRIER_BENCH_OUT";
    private const string BenchLabelEnvVar = "GSHARP_BARRIER_BENCH_LABEL";
    private const string TsvHeader = "label\tpath\tkernel\tblock\trounds\twork\tgrid\tfirstMs\tminMs\tmedianMs";

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private static readonly int[] s_blockSizes = [32, 256, 1024];
    private static readonly int[] s_roundCounts = [1, 10, 1000];
    private static readonly int[] s_workCounts = [0, 64];
    private static readonly int[] s_gridSizes = [1, 8];
    private static readonly string[] s_paths = ["native", "managed"];

    private static readonly (string Name, LaunchKernel Launch)[] s_kernels =
    [
        (
            "nobarrier",
            static (d, o, r, w, g, b) =>
                BarrierModelBenchmarkKernels.LaunchNoBarrierKernel(d, o, r, w, gridDimX: (uint)g, blockDimX: (uint)b)
        ),
        (
            "barrier",
            static (d, o, r, w, g, b) =>
                BarrierModelBenchmarkKernels.LaunchBarrierLoopKernel(d, o, r, w, gridDimX: (uint)g, blockDimX: (uint)b)
        ),
        (
            "shuffle",
            static (d, o, r, w, g, b) =>
                BarrierModelBenchmarkKernels.LaunchShuffleLoopKernel(d, o, r, w, gridDimX: (uint)g, blockDimX: (uint)b)
        ),
        (
            "activemask",
            static (d, o, r, w, g, b) =>
                BarrierModelBenchmarkKernels.LaunchActiveMaskLoopKernel(d, o, r, w, gridDimX: (uint)g, blockDimX: (uint)b)
        ),
    ];

    private delegate void LaunchKernel(int[] data, int[] output, int rounds, int work, int grid, int block);

    private sealed record SnapshotBaseline(string Label, Dictionary<string, string> Outputs);

    [SkippableFact]
    [Category(TestCategory.Benchmark)]
    [Scenario(TestScenario.HappyPath)]
    public void BarrierModelBenchmark_WhenOutPathSet_WritesTimingsAndMatchesBaseline()
    {
        string? outPath = Environment.GetEnvironmentVariable(BenchOutEnvVar);
        Skip.If(string.IsNullOrEmpty(outPath), $"{BenchOutEnvVar} is not set.");

        string label = ResolveConfigurationLabel();
        DiffPaths.EnsureNativeLibrary(DiffPaths.NativeLibraryPath(typeof(BarrierModelBenchmarkTests).Assembly));

        var lines = new List<string>();
        var nativeOutputs = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var snapshotOutputs = new Dictionary<string, int[]>(StringComparer.Ordinal);

        foreach (var (name, launch) in s_kernels)
        {
            foreach (int block in s_blockSizes)
            {
                int[] data = CreateInputData(block);
                foreach (int rounds in s_roundCounts)
                {
                    foreach (int work in s_workCounts)
                    {
                        foreach (int grid in s_gridSizes)
                        {
                            if (grid == 8 && block != 256)
                            {
                                continue;
                            }

                            string regimeKey = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{name}|{block}|{rounds}|{work}|{grid}"
                            );

                            foreach (string path in s_paths)
                            {
                                int[] output = new int[grid * block];
                                var sw = Stopwatch.StartNew();
                                RunOnce(launch, path, data, output, rounds, work, grid, block);
                                double firstMs = sw.Elapsed.TotalMilliseconds;

                                int reps = rounds == 1000 && block == 1024 ? 3 : 5;
                                var samples = new List<double>(reps);
                                for (int r = 0; r < reps; r++)
                                {
                                    sw.Restart();
                                    RunOnce(launch, path, data, output, rounds, work, grid, block);
                                    samples.Add(sw.Elapsed.TotalMilliseconds);
                                }

                                samples.Sort();
                                if (string.Equals(path, "native", StringComparison.Ordinal))
                                {
                                    nativeOutputs[regimeKey] = (int[])output.Clone();
                                }
                                else
                                {
                                    Assert.Equal(nativeOutputs[regimeKey], output);
                                }

                                string snapshotKey = $"{path}|{regimeKey}";
                                snapshotOutputs[snapshotKey] = (int[])output.Clone();

                                double minMs = samples[0];
                                double medianMs = samples[samples.Count / 2];
                                lines.Add(
                                    string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"{label}\t{path}\t{name}\t{block}\t{rounds}\t{work}\t{grid}\t{firstMs:F3}\t{minMs:F3}\t{medianMs:F3}"
                                    )
                                );
                            }
                        }
                    }
                }
            }
        }

        string? outDirectory = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDirectory))
        {
            Directory.CreateDirectory(outDirectory);
        }

        bool needsHeader = !File.Exists(outPath) || new FileInfo(outPath).Length == 0;
        if (needsHeader)
        {
            var withHeader = new List<string>(lines.Count + 1) { TsvHeader };
            withHeader.AddRange(lines);
            File.AppendAllLines(outPath, withHeader);
        }
        else
        {
            File.AppendAllLines(outPath, lines);
        }

        RecordOrCompareSnapshots($"{outPath}.snapshots.json", label, snapshotOutputs);
        Assert.NotEmpty(lines);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void BarrierModelBenchmark_FastSmokeRegime_MatchesAcrossPathsAndDetectsMismatch()
    {
        const int Block = 32;
        const int Rounds = 1;
        const int Work = 0;
        const int Grid = 1;

        DiffPaths.EnsureNativeLibrary(DiffPaths.NativeLibraryPath(typeof(BarrierModelBenchmarkTests).Assembly));

        var (name, launch) = s_kernels[1];
        int[] data = CreateInputData(Block);
        int[] nativeOutput = new int[Grid * Block];
        RunOnce(launch, "native", data, nativeOutput, Rounds, Work, Grid, Block);

        int[] managedOutput = new int[Grid * Block];
        RunOnce(launch, "managed", data, managedOutput, Rounds, Work, Grid, Block);

        Assert.Equal(nativeOutput, managedOutput);
        var smokeOutputs = new Dictionary<string, int[]>(2, StringComparer.Ordinal)
        {
            [$"native|{name}|{Block}|{Rounds}|{Work}|{Grid}"] = nativeOutput,
            [$"managed|{name}|{Block}|{Rounds}|{Work}|{Grid}"] = managedOutput,
        };

        string tempDir = Path.Combine(Path.GetTempPath(), $"gsharp-bench-smoke-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            string snapshotPath = Path.Combine(tempDir, "bench.tsv.snapshots.json");

            RecordOrCompareSnapshots(snapshotPath, "default", smokeOutputs);
            RecordOrCompareSnapshots(snapshotPath, "both-stack", smokeOutputs);

            foreach (var (key, originalBuffer) in smokeOutputs)
            {
                var mutatedOutputs = new Dictionary<string, int[]>(smokeOutputs, StringComparer.Ordinal);
                int[] mutatedBuffer = (int[])originalBuffer.Clone();
                mutatedBuffer[0] ^= 1;
                mutatedOutputs[key] = mutatedBuffer;

                Assert.Throws<Xunit.Sdk.EqualException>(
                    () => RecordOrCompareSnapshots(snapshotPath, "both-stack", mutatedOutputs)
                );
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    internal static void RecordOrCompareSnapshots(
        string snapshotPath,
        string label,
        IReadOnlyDictionary<string, int[]> outputs
    )
    {
        var digests = new Dictionary<string, string>(outputs.Count, StringComparer.Ordinal);
        foreach (var (key, buffer) in outputs)
        {
            digests[key] = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(buffer.AsSpan())));
        }

        if (!File.Exists(snapshotPath))
        {
            string? directory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var createStream = File.Create(snapshotPath);
            JsonSerializer.Serialize(createStream, new SnapshotBaseline(label, digests), s_jsonOptions);
            return;
        }

        SnapshotBaseline? baseline;
        using (var readStream = File.OpenRead(snapshotPath))
        {
            baseline = JsonSerializer.Deserialize<SnapshotBaseline>(readStream, s_jsonOptions);
        }

        Assert.NotNull(baseline);
        Assert.Equal(baseline.Outputs.Count, digests.Count);
        foreach (var (key, expectedDigest) in baseline.Outputs)
        {
            Assert.True(
                digests.TryGetValue(key, out string? actualDigest),
                $"Missing output buffer '{key}' compared to baseline '{baseline.Label}'."
            );
            Assert.Equal(expectedDigest, actualDigest);
        }
    }

    private static int[] CreateInputData(int block)
    {
        int[] data = new int[block];
        for (int i = 0; i < block; i++)
        {
            data[i] = (i * 7) + 1;
        }

        return data;
    }

    private static void RunOnce(
        LaunchKernel launch,
        string path,
        int[] data,
        int[] output,
        int rounds,
        int work,
        int grid,
        int block
    )
    {
        if (string.Equals(path, "native", StringComparison.Ordinal))
        {
            launch(data, output, rounds, work, grid, block);
        }
        else
        {
            ManagedKernelRun.Run(() => launch(data, output, rounds, work, grid, block));
        }
    }

    private static string ResolveConfigurationLabel()
    {
        string? explicitLabel = Environment.GetEnvironmentVariable(BenchLabelEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitLabel))
        {
            return explicitLabel.AsSpan().Trim().ToString();
        }

        bool nativeStack = false;
        bool managedStack = false;
        foreach (var attr in typeof(BarrierModelBenchmarkTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attr.Key, "GSharpNativeBarrierModel", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(attr.Value, "Stack", StringComparison.OrdinalIgnoreCase))
            {
                nativeStack = true;
            }
            else if (string.Equals(attr.Key, "GSharpManagedBarrierModel", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(attr.Value, "Stack", StringComparison.OrdinalIgnoreCase))
            {
                managedStack = true;
            }
        }

        return (nativeStack, managedStack) switch
        {
            (false, false) => "default",
            (true, false) => "stack",
            (false, true) => "managed-stack",
            (true, true) => "both-stack",
        };
    }
}
