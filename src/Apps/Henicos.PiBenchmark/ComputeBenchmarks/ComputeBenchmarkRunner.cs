namespace Henicos.PiBenchmark.ComputeBenchmarks;

using System.Diagnostics;

public static class ComputeBenchmarkRunner
{
    public readonly record struct BenchmarkStats(double Min, double Median, double Mean)
    {
        public static BenchmarkStats Calculate(double[] samples)
        {
            if (samples.Length == 0)
            {
                return default;
            }

            var sorted = (double[])samples.Clone();
            Array.Sort(sorted);

            var min = sorted[0];
            var median = sorted.Length % 2 == 0
                ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
                : sorted[sorted.Length / 2];
            double sum = 0;
            foreach (var v in sorted)
            {
                sum += v;
            }

            var mean = sum / sorted.Length;
            return new BenchmarkStats(min, median, mean);
        }
    }

    private sealed record BenchmarkResult(
        string Name,
        double CpuMeanMs,
        BenchmarkStats NativeStatsE2E,
        BenchmarkStats GsharpStatsE2E,
        BenchmarkStats NativeStatsPure,
        BenchmarkStats GsharpStatsPure,
        bool Success,
        string ErrorMessage = "");

    public static bool RunSuite(string suiteName, int iterations = 10)
    {
        var benchmarks = new List<IComputeBenchmark>();

        if (suiteName is "all" or "sgemm")
        {
            benchmarks.Add(new SgemmBenchmark());
        }

        if (suiteName is "all" or "sha256")
        {
            benchmarks.Add(new Sha256Benchmark());
        }

        if (suiteName is "all" or "bs")
        {
            benchmarks.Add(new BlackScholesBenchmark());
        }

        if (suiteName is "all" or "reduce")
        {
            benchmarks.Add(new ReductionBenchmark());
        }

        if (benchmarks.Count == 0)
        {
            Console.WriteLine(
                $"No benchmarks found for suite '{suiteName}'. Available: sgemm, sha256, bs, reduce, all");
            return false;
        }

        Console.WriteLine(
            $"==========================================================================================");
        Console.WriteLine(
            $" Henicos / G# Compute Benchmark Suite ({benchmarks.Count} benchmarks, {iterations} iterations/sample)");
        Console.WriteLine(
            $"==========================================================================================");

        var allSucceeded = true;
        var results = new List<BenchmarkResult>();

        foreach (var benchmark in benchmarks)
        {
            Console.WriteLine($"\n[>>> {benchmark.Name} <<<]");
            Console.WriteLine("  Initializing...");
            benchmark.Initialize();

            // 1. CPU Reference
            Console.WriteLine("  [Phase 0] Running CPU Reference Baseline (1 sample)...");
            var swCpu = Stopwatch.StartNew();
            benchmark.RunReferenceCpu();
            swCpu.Stop();
            var cpuTime = swCpu.Elapsed.TotalMilliseconds;
            Console.WriteLine($"  CPU Reference Time: {cpuTime:F2} ms");

            // --- PHASE 1: End-to-End (E2E) Measurements ---
            Console.WriteLine("\n  [Phase 1] E2E Measurements (Includes Alloc, HtoD, Kernel, DtoH, Free)");

            var (nativeSuccess, nativeStatsE2E, nativeError) = MeasureExecution(benchmark.RunNativeCuda, iterations);
            if (nativeSuccess)
            {
                Console.WriteLine(
                    $"    Native CUDA (E2E): Min={nativeStatsE2E.Min:F2} ms | Med={nativeStatsE2E.Median:F2} ms | Mean={nativeStatsE2E.Mean:F2} ms");
            }
            else
            {
                Console.WriteLine($"  Native CUDA (E2E) Failed: {nativeError}");
                allSucceeded = false;
            }

            var (gsharpSuccess, gsharpStatsE2E, gsharpError) = MeasureExecution(benchmark.RunGSharp, iterations);
            if (gsharpSuccess)
            {
                Console.WriteLine(
                    $"    G# CUDA     (E2E): Min={gsharpStatsE2E.Min:F2} ms | Med={gsharpStatsE2E.Median:F2} ms | Mean={gsharpStatsE2E.Mean:F2} ms");
            }
            else
            {
                Console.WriteLine($"  G# CUDA     (E2E) Failed: {gsharpError}");
                allSucceeded = false;
            }

            // --- PHASE 2: Pure Kernel Measurements ---
            Console.WriteLine("\n  [Phase 2] Pure Kernel Measurements (Resident Device Buffers only)");

            BenchmarkStats nativeStatsPure = default;
            BenchmarkStats gsharpStatsPure = default;
            var pureError = "";
            var pureSuccess = false;

            if (nativeSuccess && gsharpSuccess)
            {
                try
                {
                    benchmark.InitializeDeviceMemory();

                    var (nSuccess, nStats, _) = MeasureExecution(benchmark.RunNativeCuda_Pure, iterations);
                    var (gSuccess, gStats, _) = MeasureExecution(benchmark.RunGSharp_Pure, iterations);

                    nativeStatsPure = nStats;
                    gsharpStatsPure = gStats;
                    pureSuccess = nSuccess && gSuccess;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Pure Kernel Execution Failed: {ex.Message}");
                    pureError = ex.Message;
                    allSucceeded = false;
                    pureSuccess = false;
                }
                finally
                {
                    benchmark.CleanupDeviceMemory();
                }
            }

            if (pureSuccess)
            {
                Console.WriteLine(
                    $"    Native CUDA (Pure): Min={nativeStatsPure.Min:F2} ms | Med={nativeStatsPure.Median:F2} ms | Mean={nativeStatsPure.Mean:F2} ms");
                Console.WriteLine(
                    $"    G# CUDA     (Pure): Min={gsharpStatsPure.Min:F2} ms | Med={gsharpStatsPure.Median:F2} ms | Mean={gsharpStatsPure.Mean:F2} ms");
            }

            // 4. Verification
            if (pureSuccess)
            {
                Console.Write("\n  Verifying Correctness (CPU == G# == Native)... ");
                try
                {
                    benchmark.Verify();
                    Console.WriteLine("PASS");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL - {ex.Message}");
                    pureError = ex.Message;
                    allSucceeded = false;
                }
            }
            else
            {
                Console.WriteLine("\n  Verification: SKIPPED (due to execution failure)");
            }

            var errorMessage = !string.IsNullOrEmpty(nativeError) ? nativeError :
                !string.IsNullOrEmpty(gsharpError) ? gsharpError : pureError;

            results.Add(new BenchmarkResult(
                benchmark.Name,
                cpuTime,
                nativeStatsE2E,
                gsharpStatsE2E,
                nativeStatsPure,
                gsharpStatsPure,
                pureSuccess && string.IsNullOrEmpty(errorMessage),
                errorMessage));
        }

        // Summary Table
        Console.WriteLine("\n");
        Console.WriteLine(
            "=========================================================================================================================================");
        Console.WriteLine(
            "                                           BENCHMARK COMPARISON & OVERHEAD SUMMARY                                                       ");
        Console.WriteLine(
            "=========================================================================================================================================");
        Console.WriteLine(string.Format("{0,-25} | {1,9} | {2,20} | {3,20} | {4,16} | {5,20} | {6,16}",
            "Benchmark Name", "CPU (ms)", "Native E2E (ms)", "G# E2E (ms)", "Ratio (E2E)", "Native Pure (ms)",
            "G# Pure (ms)", "Ratio (Pure)"));
        Console.WriteLine(new string('-', 140));

        foreach (var r in results)
        {
            if (r.Success)
            {
                var ratioE2E = r.NativeStatsE2E.Mean > 0 ? r.GsharpStatsE2E.Mean / r.NativeStatsE2E.Mean : 1.0;
                var ratioPure = r.NativeStatsPure.Mean > 0 ? r.GsharpStatsPure.Mean / r.NativeStatsPure.Mean : 1.0;

                var e2EStr = $"{r.NativeStatsE2E.Mean:F2} / {r.GsharpStatsE2E.Mean:F2}";
                var pureStr = $"{r.NativeStatsPure.Mean:F2} / {r.GsharpStatsPure.Mean:F2}";

                Console.WriteLine(string.Format(
                    "{0,-25} | {1,9:F2} | {2,20:F2} | {3,20:F2} | {4,15:F2}x | {5,20:F2} | {6,15:F2}x",
                    r.Name, r.CpuMeanMs, r.NativeStatsE2E.Mean, r.GsharpStatsE2E.Mean, ratioE2E, r.NativeStatsPure.Mean,
                    r.GsharpStatsPure.Mean, ratioPure));
            }
            else
            {
                Console.WriteLine(string.Format("{0,-25} | {1,9:F2} | {2,20} | {3,20} | {4,16} | {5,20} | {6,16}",
                    r.Name, r.CpuMeanMs, "FAIL", "FAIL", "N/A", "FAIL", "FAIL", "N/A"));
            }
        }

        Console.WriteLine(
            "=========================================================================================================================================");

        return allSucceeded;
    }

    private static (bool Success, BenchmarkStats Stats, string ErrorMessage) MeasureExecution(Action action,
        int iterations)
    {
        var samples = new double[iterations];
        try
        {
            action(); // Warmup
            for (var i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                samples[i] = sw.Elapsed.TotalMilliseconds;
            }

            return (true, BenchmarkStats.Calculate(samples), string.Empty);
        }
        catch (Exception ex)
        {
            return (false, default, ex.Message);
        }
    }
}
