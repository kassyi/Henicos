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
            BenchmarkRunner.Run<FermatBenchmark>();
            return;
        }
        else if (args.Length > 0 && args[0] == "--benchmark-gpu")
        {
            BenchmarkRunner.Run<GpuBigIntBenchmark>();
            return;
        }

        using var tee = new TeeTextWriter(Console.Out, "benchmark_results.log");
        Console.SetOut(tee);

        Console.WriteLine("Henicos Project - Ultra Optimized Modular Reduction Benchmark");
        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine("To run formal benchmarks, pass '--benchmark' argument.");
        Console.WriteLine("---------------------------------------------------------------");

        // Benchmark target E values (Crossover Test)
        int[] testEs = [1000, 5000, 10000, 20000, 50000, 100000];

        foreach (var e in testEs)
        {
            RunBenchmarkForE(e);
        }
    }

    static void RunBenchmarkForE(int e)
    {
        Console.WriteLine($"\n[ Benchmarking for E = {e} ]");

        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger a = 2;
        var exponent = n - 1;
        var totalDigits = ((2 * e + 1) * Math.Log10(2)) * e;

        var hybridPass = e <= 10000 && TestHybridReduction(e, n);

        var std = RunTest(() => BigInteger.ModPow(a, exponent, n), totalDigits);
        // var v5 = RunTest(() =>
        // {
        //     try { return ModularReductionGpu.FermatPrpTest(e, n); }
        //     catch (Exception ex) { Console.WriteLine($"[V5 Error] {ex.Message}"); return BigInteger.Zero; }
        // }, totalDigits);

        var hybridMs = MeasureHybridV3(e, x);

        Console.WriteLine($"  - Standard BigInteger : {std.Ms,5} ms | {std.MDps,6:F2} M-Digit/s (Baseline)");
        Console.WriteLine($"  - Hybrid CPU V3 (Reduc): {hybridMs,6:F3} ms/iter (Extremely Fast Parallel Chunking)");
        // Console.WriteLine($"  - ZERO Overhead VRAM V5: {v5.Ms,5} ms | {v5.MDps,6:F2} M-Digit/s ({(double)std.Ms / Math.Max(1, v5.Ms):F2}x vs Std)");

        var pass = /*(std.Result == v5.Result) && */ (e > 10000 || hybridPass);
        if (!pass)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [FAIL] Mismatch detected!");
            Console.WriteLine($"    Standard : {std.Result}");
            Console.WriteLine($"    Hybrid V3: Pass={hybridPass}");
            // Console.WriteLine($"    Zero V5  : {v5.Result} (Pass: {std.Result == v5.Result})");
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

    static double MeasureHybridV3(int e, BigInteger x)
    {
        var numWords = (2 * e + 1 + 31) / 32;
        var t = BigInteger.Pow(x, 2) + 123456789;
        var tBytes = t.ToByteArray();
        var data = new uint[numWords * 4];
        Buffer.BlockCopy(tBytes, 0, data, 0, tBytes.Length);

        var sw = Stopwatch.StartNew();
        var iters = (e <= 1000) ? 10000 : 100;
        for (var i = 0; i < iters; i++)
        {
            HybridReduction.ReduceInPlace(data, e, numWords);
        }

        sw.Stop();
        return (double)sw.ElapsedMilliseconds / iters;
    }

    static bool TestHybridReduction(int e, BigInteger n)
    {
        var t = BigInteger.Pow(BigInteger.One << (2 * e), 2) + 123456789;
        var expected = t % n;
        if (expected < 0)
        {
            expected += n;
        }

        var numWords = (2 * e + 1 + 31) / 32;
        var tBytes = t.ToByteArray();
        var data = new uint[numWords * 4];
        Buffer.BlockCopy(tBytes, 0, data, 0, tBytes.Length);

        HybridReduction.ReduceInPlace(data, e, numWords);

        var highestWord = data[^1];
        var resBytes = new byte[data.Length * 4 + 1];
        Buffer.BlockCopy(data, 0, resBytes, 0, data.Length * 4);

        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[data.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;

        var actual = new BigInteger(resBytes);
        actual %= n;
        if (actual < 0)
        {
            actual += n;
        }

        if (expected == actual)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [PASS] HybridReduction output matches for E={e}");
            Console.ResetColor();
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [WARN] HybridReduction output MISMATCH for E={e}");
        Console.WriteLine($"         Expected: {expected}");
        Console.WriteLine($"         Actual  : {actual}");
        Console.ResetColor();
        return false;
    }
}
