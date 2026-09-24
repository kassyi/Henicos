namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class ComputeBenchmarkRunner
{
    private static readonly Dictionary<string, Func<IComputeBenchmark>> s_benchmarkFactories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "sgemm", () => new SgemmBenchmark() },
            { "sha256", () => new Sha256Benchmark() },
            { "bs", () => new BlackScholesBenchmark() },
            { "reduce", () => new ReductionBenchmark() }
        };

    public static List<IComputeBenchmark> GetBenchmarksForSuite(string suiteName)
    {
        var benchmarks = new List<IComputeBenchmark>();

        if (suiteName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var factory in s_benchmarkFactories.Values)
            {
                benchmarks.Add(factory());
            }
        }
        else if (s_benchmarkFactories.TryGetValue(suiteName, out var factory))
        {
            benchmarks.Add(factory());
        }

        return benchmarks;
    }

    public static BenchmarkStats RunBenchmarkSamples(Action benchmarkAction, int iterations)
    {
        // Warmup
        benchmarkAction();

        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            benchmarkAction();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        return BenchmarkStats.Calculate(samples);
    }

    public static bool RunSuite(string suiteName, int iterations = 10, bool jsonOutput = false)
    {
        var logOut = jsonOutput ? Console.Error : Console.Out;
        var results = RunSuite(suiteName, iterations, logOut);
        if (results.Count == 0)
        {
            return false;
        }

        if (jsonOutput)
        {
            ComputeBenchmarkReporter.ReportJson(results, Console.Out);
        }
        else
        {
            ComputeBenchmarkReporter.ReportTable(results, logOut);
        }

        var allSucceeded = true;
        foreach (var r in results)
        {
            if (r.Success)
            {
                continue;
            }

            allSucceeded = false;
            break;
        }

        return allSucceeded;
    }

    public static List<BenchmarkResult> RunSuite(string suiteName, int iterations, TextWriter logOut)
    {
        var benchmarks = GetBenchmarksForSuite(suiteName);

        if (benchmarks.Count == 0)
        {
            logOut.WriteLine($"No benchmarks found for suite '{suiteName}'. Available: sgemm, sha256, bs, reduce, all");
            return [];
        }

        PrintSuiteHeader(logOut, benchmarks.Count, iterations);

        var results = new List<BenchmarkResult>(benchmarks.Count);
        foreach (var benchmark in benchmarks)
        {
            results.Add(RunBenchmark(benchmark, iterations, logOut));
        }

        return results;
    }

    public static BenchmarkResult RunBenchmark(IComputeBenchmark benchmark, int iterations, TextWriter logOut)
    {
        logOut.WriteLine($"\n[>>> {benchmark.Name} <<<]");
        logOut.WriteLine("  Initializing...");
        benchmark.Initialize();

        var cpuTime = ExecuteCpuBaseline(benchmark, logOut);

        var errorMessage = "";
        var nativeSuccess = ExecuteNativeCuda(benchmark, iterations, logOut, ref errorMessage, out var nativeStats);
        var gsharpSuccess = ExecuteGSharp(benchmark, iterations, logOut, ref errorMessage, out var gsharpStats);

        var verified = VerifyResults(benchmark, nativeSuccess, gsharpSuccess, logOut, ref errorMessage);
        var isSuccess = nativeSuccess && gsharpSuccess && verified && string.IsNullOrEmpty(errorMessage);

        return new BenchmarkResult(
            benchmark.Name,
            cpuTime,
            nativeStats,
            gsharpStats,
            isSuccess,
            errorMessage);
    }

    private static void PrintSuiteHeader(TextWriter logOut, int benchmarkCount, int iterations)
    {
        logOut.WriteLine("========================================");
        logOut.WriteLine(
            $" Henicos / G# Compute Benchmark Suite ({benchmarkCount} benchmarks, {iterations} iterations/sample)");
        logOut.WriteLine("========================================");
    }

    private static double ExecuteCpuBaseline(IComputeBenchmark benchmark, TextWriter logOut)
    {
        logOut.WriteLine("  Running CPU Reference Baseline (1 sample)...");
        var swCpu = Stopwatch.StartNew();
        benchmark.RunReferenceCpu();
        swCpu.Stop();
        var cpuTime = swCpu.Elapsed.TotalMilliseconds;
        logOut.WriteLine($"  CPU Reference Time: {cpuTime:F2} ms");
        return cpuTime;
    }

    private static bool ExecuteNativeCuda(
        IComputeBenchmark benchmark,
        int iterations,
        TextWriter logOut,
        ref string errorMessage,
        out BenchmarkStats stats)
    {
        logOut.WriteLine($"  Running Native CUDA C++ (Warmup + {iterations} iterations)...");
        try
        {
            stats = RunBenchmarkSamples(benchmark.RunNativeCuda, iterations);
            logOut.WriteLine(
                $"  Native CUDA Time: Min={stats.Min:F2} ms | Med={stats.Median:F2} ms | Mean={stats.Mean:F2} ms");
            return true;
        }
        catch (Exception ex)
        {
            logOut.WriteLine($"  Native CUDA Execution Failed: {ex.Message}");
            errorMessage = $"Native CUDA: {ex.Message}";
            stats = default;
            return false;
        }
    }

    private static bool ExecuteGSharp(
        IComputeBenchmark benchmark, int iterations, TextWriter logOut, ref string errorMessage,
        out BenchmarkStats stats)
    {
        logOut.WriteLine($"  Running G# CUDA Transpiled (Warmup + {iterations} iterations)...");
        try
        {
            stats = RunBenchmarkSamples(benchmark.RunGSharp, iterations);
            logOut.WriteLine(
                $"  G# CUDA Time:     Min={stats.Min:F2} ms | Med={stats.Median:F2} ms | Mean={stats.Mean:F2} ms");
            return true;
        }
        catch (Exception ex)
        {
            logOut.WriteLine($"  G# CUDA Execution Failed: {ex.Message}");
            errorMessage = string.IsNullOrEmpty(errorMessage) ? ex.Message : $"{errorMessage} | G#: {ex.Message}";
            stats = default;
            return false;
        }
    }

    private static bool VerifyResults(IComputeBenchmark benchmark, bool nativeSuccess, bool gsharpSuccess,
        TextWriter logOut, ref string errorMessage)
    {
        if (!nativeSuccess || !gsharpSuccess)
        {
            logOut.WriteLine("  Verification: SKIPPED (due to execution failure)");
            return false;
        }

        logOut.Write("  Verifying Correctness (CPU == G# == Native)... ");
        try
        {
            benchmark.Verify();
            logOut.WriteLine("PASS");
            return true;
        }
        catch (Exception ex)
        {
            logOut.WriteLine($"FAIL - {ex.Message}");
            errorMessage = ex.Message;
            return false;
        }
    }
}
