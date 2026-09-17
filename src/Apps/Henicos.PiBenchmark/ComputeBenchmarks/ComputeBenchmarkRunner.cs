namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class ComputeBenchmarkRunner
{
    public static bool RunSuite(string suiteName)
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

        Console.WriteLine($"--- Starting Compute Benchmark Suite ({benchmarks.Count} benchmarks) ---");

        var allSucceeded = true;

        foreach (var benchmark in benchmarks)
        {
            Console.WriteLine($"\n[{benchmark.Name}] Initializing...");
            benchmark.Initialize();

            Console.WriteLine($"[{benchmark.Name}] Running CPU Reference Baseline...");
            var swRef = Stopwatch.StartNew();
            benchmark.RunReferenceCpu();
            swRef.Stop();
            Console.WriteLine($"[{benchmark.Name}] CPU Reference Time: {swRef.Elapsed.TotalMilliseconds:F2} ms");

            Console.WriteLine($"[{benchmark.Name}] Running G# CPU Mode (Warmup & Compilation)...");
            var executionSucceeded = false;
            try
            {
                // Warmup run to trigger AOT load / initialization outside timing window
                benchmark.RunGSharpCpu();
                executionSucceeded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{benchmark.Name}] G# CPU Mode Execution Failed: {ex.Message}");
                allSucceeded = false;
            }

            if (executionSucceeded)
            {
                var swGsharp = Stopwatch.StartNew();
                benchmark.RunGSharpCpu();
                swGsharp.Stop();
                Console.WriteLine($"[{benchmark.Name}] G# CPU Mode Time (Steady-State): {swGsharp.Elapsed.TotalMilliseconds:F2} ms");

                Console.WriteLine($"[{benchmark.Name}] Verifying Logical Correctness...");
                try
                {
                    benchmark.Verify();
                    Console.WriteLine($"[{benchmark.Name}] Verification: PASS");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{benchmark.Name}] Verification: FAIL - {ex.Message}");
                    allSucceeded = false;
                }
            }
            else
            {
                Console.WriteLine($"[{benchmark.Name}] Verification: SKIPPED (due to execution failure)");
            }
        }

        Console.WriteLine("\n--- Compute Benchmark Suite Completed ---");
        return allSucceeded;
    }
}
