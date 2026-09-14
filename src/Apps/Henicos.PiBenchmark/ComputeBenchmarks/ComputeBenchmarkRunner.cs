namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class ComputeBenchmarkRunner
{
    public static void RunSuite(string suiteName)
    {
        var benchmarks = new List<IComputeBenchmark>();

        // TODO: Register benchmarks here once implemented
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
            return;
        }

        Console.WriteLine($"--- Starting Compute Benchmark Suite ({benchmarks.Count} benchmarks) ---");

        foreach (var benchmark in benchmarks)
        {
            Console.WriteLine($"\n[{benchmark.Name}] Initializing...");
            benchmark.Initialize();

            Console.WriteLine($"[{benchmark.Name}] Running CPU Reference Baseline...");
            var swRef = Stopwatch.StartNew();
            benchmark.RunReferenceCpu();
            swRef.Stop();
            Console.WriteLine($"[{benchmark.Name}] CPU Reference Time: {swRef.Elapsed.TotalMilliseconds:F2} ms");

            Console.WriteLine($"[{benchmark.Name}] Running G# CPU Mode...");
            var swGsharp = Stopwatch.StartNew();
            try
            {
                benchmark.RunGSharpCpu();
                swGsharp.Stop();
                Console.WriteLine($"[{benchmark.Name}] G# CPU Mode Time: {swGsharp.Elapsed.TotalMilliseconds:F2} ms");
            }
            catch (Exception ex)
            {
                swGsharp.Stop();
                Console.WriteLine($"[{benchmark.Name}] G# CPU Mode Skipped (Environment Unsupported): {ex.Message}");
            }

            Console.WriteLine($"[{benchmark.Name}] Verifying Logical Correctness...");
            try
            {
                benchmark.Verify();
                Console.WriteLine($"[{benchmark.Name}] Verification: PASS");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{benchmark.Name}] Verification: FAIL - {ex.Message}");
            }
        }

        Console.WriteLine("\n--- Compute Benchmark Suite Completed ---");
    }
}
