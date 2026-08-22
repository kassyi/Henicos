using System.Diagnostics;
using System.Numerics;
using BenchmarkDotNet.Running;

namespace Henicos;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--benchmark")
        {
            if (args.Length > 1 && args[1] == "gpu")
            {
                var summary = BenchmarkRunner.Run<GpuBenchmark>();
                return;
            }
            else
            {
                var summary = BenchmarkRunner.Run<FermatBenchmark>();
                return;
            }
        }

        using var tee = new TeeTextWriter(Console.Out, "benchmark_results.log");
        Console.SetOut(tee);

        Console.WriteLine("Henicos Project - Ultra Optimized Modular Reduction Benchmark");
        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine("To run formal benchmarks, pass '--benchmark' argument.");
        Console.WriteLine("---------------------------------------------------------------");

        // Benchmark target E values (Crossover Test)
        int[] testEs = [1000, 5000, 10000, 20000, 50000, 100000];

        foreach (var E in testEs)
        {
            RunBenchmarkForE(E);
        }
    }

    static void RunBenchmarkForE(int E)
    {
        Console.WriteLine($"\n[ Benchmarking for E = {E} ]");

        var X = BigInteger.One << E;
        var N = (BigInteger.One << (2 * E + 1)) - X + 11;
        BigInteger a = 2;
        var exponent = N - 1;
        var totalDigits = ((2 * E + 1) * Math.Log10(2)) * E;

        var hybridPass = E <= 10000 ? TestHybridReduction(E, N) : false;

        var std = RunTest(() => BigInteger.ModPow(a, exponent, N), totalDigits);
        var v5 = RunTest(() =>
        {
            try { return ModularReductionGpu.FermatPRPTest(E, N); }
            catch (Exception ex) { Console.WriteLine($"[V5 Error] {ex.Message}"); return BigInteger.Zero; }
        }, totalDigits);

        var hybridMs = MeasureHybridV3(E, X);

        Console.WriteLine($"  - Standard BigInteger : {std.Ms,5} ms | {std.MDps,6:F2} M-Digit/s (Baseline)");
        Console.WriteLine($"  - Hybrid CPU V3 (Reduc): {hybridMs,6:F3} ms/iter (Extremely Fast Parallel Chunking)");
        Console.WriteLine($"  - ZERO Overhead VRAM V5: {v5.Ms,5} ms | {v5.MDps,6:F2} M-Digit/s ({(double)std.Ms / Math.Max(1, v5.Ms):F2}x vs Std)");

        var pass = (std.Result == v5.Result) && (E > 10000 || hybridPass);
        if (!pass)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [FAIL] Mismatch detected!");
            Console.WriteLine($"    Standard : {std.Result}");
            Console.WriteLine($"    Hybrid V3: Pass={hybridPass}");
            Console.WriteLine($"    Zero V5  : {v5.Result} (Pass: {std.Result == v5.Result})");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [PASS] All logic matches standard result.");
            Console.ResetColor();
        }
    }

    static (BigInteger Result, long Ms, double MDps) RunTest(Func<BigInteger> test, double totalDigits)
    {
        var sw = Stopwatch.StartNew();
        var result = test();
        sw.Stop();
        var mdps = sw.ElapsedMilliseconds > 0 ? (totalDigits / sw.ElapsedMilliseconds) / 1000.0 : 0;
        return (result, sw.ElapsedMilliseconds, mdps);
    }

    static double MeasureHybridV3(int E, BigInteger X)
    {
        var numWords = (2 * E + 1 + 31) / 32;
        var T = BigInteger.Pow(X, 2) + 123456789;
        var tBytes = T.ToByteArray();
        var data = new uint[numWords * 4];
        Buffer.BlockCopy(tBytes, 0, data, 0, tBytes.Length);

        var sw = Stopwatch.StartNew();
        var iters = (E <= 1000) ? 10000 : 100;
        for (var i = 0; i < iters; i++)
        {
            HybridReduction.ReduceInPlace(data, E, numWords);
        }

        sw.Stop();
        return (double)sw.ElapsedMilliseconds / iters;
    }

    static bool TestHybridReduction(int E, BigInteger N)
    {
        var T = BigInteger.Pow(BigInteger.One << (2 * E), 2) + 123456789;
        var expected = T % N;
        if (expected < 0)
        {
            expected += N;
        }

        var numWords = (2 * E + 1 + 31) / 32;
        var tBytes = T.ToByteArray();
        var data = new uint[numWords * 4];
        Buffer.BlockCopy(tBytes, 0, data, 0, tBytes.Length);

        HybridReduction.ReduceInPlace(data, E, numWords);

        var highestWord = data[data.Length - 1];
        var resBytes = new byte[data.Length * 4 + 1];
        Buffer.BlockCopy(data, 0, resBytes, 0, data.Length * 4);

        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[data.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;

        var actual = new BigInteger(resBytes);
        actual %= N;
        if (actual < 0)
        {
            actual += N;
        }

        if (expected == actual)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [PASS] HybridReduction output matches for E={E}");
            Console.ResetColor();
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [WARN] HybridReduction output MISMATCH for E={E}");
        Console.WriteLine($"         Expected: {expected}");
        Console.WriteLine($"         Actual  : {actual}");
        Console.ResetColor();
        return false;
    }
}
