using System.Text.Json;
namespace Henicos.Benchmarks.ComputeBenchmarks;

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

            double min = sorted[0];
            double median = sorted.Length % 2 == 0
                ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
                : sorted[sorted.Length / 2];
            double sum = 0;
            foreach (var v in sorted)
            {
                sum += v;
            }

            double mean = sum / sorted.Length;
            return new BenchmarkStats(min, median, mean);
        }
    }

    private sealed record BenchmarkResult(
        string Name,
        double CpuMeanMs,
        BenchmarkStats NativeStats,
        BenchmarkStats GsharpStats,
        bool Success,
        string ErrorMessage = "");

    public static bool RunSuite(string suiteName, int iterations = 10, bool jsonOutput = false)
    {
        var logOut = jsonOutput ? Console.Error : Console.Out;
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
            logOut.WriteLine(
                $"No benchmarks found for suite '{suiteName}'. Available: sgemm, sha256, bs, reduce, all");
            return false;
        }

        logOut.WriteLine($"==========================================================================================");
        logOut.WriteLine($" Henicos / G# Compute Benchmark Suite ({benchmarks.Count} benchmarks, {iterations} iterations/sample)");
        logOut.WriteLine($"==========================================================================================");

        var allSucceeded = true;
        var results = new List<BenchmarkResult>();

        foreach (var benchmark in benchmarks)
        {
            string errorMessage = "";
            logOut.WriteLine($"\n[>>> {benchmark.Name} <<<]");
            logOut.WriteLine("  Initializing...");
            benchmark.Initialize();

            // 1. CPU Reference
            logOut.WriteLine("  Running CPU Reference Baseline (1 sample)...");
            var swCpu = Stopwatch.StartNew();
            benchmark.RunReferenceCpu();
            swCpu.Stop();
            var cpuTime = swCpu.Elapsed.TotalMilliseconds;
            logOut.WriteLine($"  CPU Reference Time: {cpuTime:F2} ms");

            // 2. Native CUDA C++
            logOut.WriteLine($"  Running Native CUDA C++ (Warmup + {iterations} iterations)...");
            var nativeSamples = new double[iterations];
            var nativeSuccess = false;
            try
            {
                // Warmup
                benchmark.RunNativeCuda();
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    benchmark.RunNativeCuda();
                    sw.Stop();
                    nativeSamples[i] = sw.Elapsed.TotalMilliseconds;
                }
                nativeSuccess = true;
            }
            catch (Exception ex)
            {
                logOut.WriteLine($"  Native CUDA Execution Failed: {ex.Message}");
                errorMessage = $"Native CUDA: {ex.Message}";
                allSucceeded = false;
            }

            var nativeStats = nativeSuccess ? BenchmarkStats.Calculate(nativeSamples) : default;
            if (nativeSuccess)
            {
                logOut.WriteLine($"  Native CUDA Time: Min={nativeStats.Min:F2} ms | Med={nativeStats.Median:F2} ms | Mean={nativeStats.Mean:F2} ms");
            }

            // 3. G# CUDA
            logOut.WriteLine($"  Running G# CUDA Transpiled (Warmup + {iterations} iterations)...");
            var gsharpSamples = new double[iterations];
            var gsharpSuccess = false;
            try
            {
                // Warmup
                benchmark.RunGSharp();
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    benchmark.RunGSharp();
                    sw.Stop();
                    gsharpSamples[i] = sw.Elapsed.TotalMilliseconds;
                }
                gsharpSuccess = true;
            }
            catch (Exception ex)
            {
                logOut.WriteLine($"  G# CUDA Execution Failed: {ex.Message}");
                errorMessage = string.IsNullOrEmpty(errorMessage) ? ex.Message : $"{errorMessage} | G#: {ex.Message}";
                allSucceeded = false;
            }

            var gsharpStats = gsharpSuccess ? BenchmarkStats.Calculate(gsharpSamples) : default;
            if (gsharpSuccess)
            {
                logOut.WriteLine($"  G# CUDA Time:     Min={gsharpStats.Min:F2} ms | Med={gsharpStats.Median:F2} ms | Mean={gsharpStats.Mean:F2} ms");
            }

            // 4. Verification
            if (nativeSuccess && gsharpSuccess)
            {
                logOut.Write("  Verifying Correctness (CPU == G# == Native)... ");
                try
                {
                    benchmark.Verify();
                    logOut.WriteLine("PASS");
                }
                catch (Exception ex)
                {
                    logOut.WriteLine($"FAIL - {ex.Message}");
                    errorMessage = ex.Message;
                    allSucceeded = false;
                }
            }
            else
            {
                logOut.WriteLine("  Verification: SKIPPED (due to execution failure)");
            }

            results.Add(new BenchmarkResult(
                benchmark.Name,
                cpuTime,
                nativeStats,
                gsharpStats,
                nativeSuccess && gsharpSuccess && string.IsNullOrEmpty(errorMessage),
                errorMessage));
        }

        if (jsonOutput)
        {
            var json = JsonSerializer.Serialize(results,
                    new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                );
            Console.WriteLine(json);
            return allSucceeded;
        }

        // Summary Table
        logOut.WriteLine("\n");
        logOut.WriteLine("========================================================================================================================");
        logOut.WriteLine("                                    BENCHMARK COMPARISON & OVERHEAD SUMMARY                                             ");
        logOut.WriteLine("========================================================================================================================");
        logOut.WriteLine(string.Format("{0,-30} | {1,10} | {2,18} | {3,18} | {4,12} | {5,10}",
            "Benchmark Name", "CPU (ms)", "Native CUDA (Mean)", "G# CUDA (Mean)", "G#/Native Ratio", "Speedup vs CPU"));
        logOut.WriteLine(new string('-', 120));

        foreach (var r in results)
        {
            if (r.Success)
            {
                double ratio = r.NativeStats.Mean > 0 ? r.GsharpStats.Mean / r.NativeStats.Mean : 1.0;
                double speedup = r.GsharpStats.Mean > 0 ? r.CpuMeanMs / r.GsharpStats.Mean : 1.0;
                logOut.WriteLine(string.Format("{0,-30} | {1,10:F2} | {2,15:F2} ms | {3,15:F2} ms | {4,11:F2}x | {5,9:F1}x",
                    r.Name, r.CpuMeanMs, r.NativeStats.Mean, r.GsharpStats.Mean, ratio, speedup));
            }
            else
            {
                logOut.WriteLine(string.Format("{0,-30} | {1,10:F2} | {2,18} | {3,18} | {4,12} | {5,10}",
                    r.Name, r.CpuMeanMs, "FAIL", "FAIL", "N/A", "N/A"));
            }
        }
        logOut.WriteLine("========================================================================================================================");

        return allSucceeded;
    }
}
