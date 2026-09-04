using Kassyi.GSharp.Stdlib.Math.BigInt;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Running;
using Cocona;

namespace Henicos.Cli;

/// <summary>Command-line interface entry point for executing Fermat tests, benchmarks, and profiling suites.</summary>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Used implicitly by CLI execution")]
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Used implicitly by CLI execution")]
internal class Program
{
    private static void Main(string[] args) => CoconaApp.Run<Program>(args);

    [Command("run", Description = "Run Fermat PRP test for a given Exponent (E)")]
    public void Run([Option('e', Description = "Fermat Exponent (E)")] int e = 1000)
    {
        Console.WriteLine($"Starting Fermat PRP test for E={e}...");
        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger a = 2;
        var exponent = n - 1;

        Console.WriteLine("Calculating expected result via CPU BigInteger...");
        var expected = BigInteger.ModPow(a, exponent, n);

        Console.WriteLine("Executing GPU GSharp Runner...");
        var runner = new FermatRunner(e);
        var (resultArray, _) = runner.RunFermatPrp();

        var actual = GetBigIntegerFromResult(resultArray, n);

        Console.WriteLine($"Expected: {expected}");
        Console.WriteLine($"Actual  : {actual}");
        Console.WriteLine($"Match?  : {expected == actual}");
    }

    [Command("profile", Description = "Profile the GSharp GPU execution and output M-Digit/s throughput")]
    public void Profile([Option('e', Description = "Fermat Exponent (E)")] int e = 10000,
                        [Option('i', Description = "Number of warmup/profile iterations")] int iterations = 2)
    {
        Console.WriteLine($"Profiling GSharp FermatRunner for E={e} (Iterations: {iterations})...");

        // Output for AI schedule hints
        var estimatedSeconds = 15 + (iterations * e / 500);
        Console.WriteLine($"[INFO] Estimated max execution time: {estimatedSeconds} seconds.");

        var runner = new FermatRunner(e);

        var numBits = (2 * e + 1);
        var numDigits = numBits * Math.Log10(2);
        var mDigitsPerLoop = (numDigits * 2) / 1_000_000.0;

        var globalSw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (globalSw.Elapsed.TotalMinutes > 5)
            {
                Console.WriteLine("[WARN] Benchmark aborted: Reached 5 minute timeout limit.");
                break;
            }

            var sw = Stopwatch.StartNew();
            runner.RunFermatPrp();
            sw.Stop();

            var throughput = mDigitsPerLoop * e / (sw.Elapsed.TotalSeconds);
            Console.WriteLine($"Iter {i}: Elapsed {sw.ElapsedMilliseconds} ms, Throughput: {throughput:F2} M-Digit/s");
        }
    }

    [Command("profile-chunk", Description = "Profile the GSharp Chunk GPU execution to avoid TDR and get immediate M-Digit/s throughput")]
    public void ProfileChunk([Option('e', Description = "Fermat Exponent (E)")] int e = 10000,
                             [Option('i', Description = "Number of warmup/profile iterations")] int iterations = 2,
                             [Option('c', Description = "Chunk Size (Loop iterations in kernel)")] int chunkSize = 100)
    {
        Console.WriteLine($"Profiling GSharp Chunk Benchmark for E={e}, ChunkSize={chunkSize} (Iterations: {iterations})...");

        // Output for AI schedule hints
        var estimatedSeconds = 15 + (iterations * 2); // JIT comp (15s) + fast chunks (2s per iter)
        Console.WriteLine($"[INFO] Estimated max execution time: {estimatedSeconds} seconds.");

        var runner = new FermatRunner(e);

        var numBits = (2 * e + 1);
        var numDigits = numBits * Math.Log10(2);
        var mDigitsPerLoop = (numDigits * 2) / 1_000_000.0;

        var globalSw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (globalSw.Elapsed.TotalMinutes > 5)
            {
                Console.WriteLine("[WARN] Benchmark aborted: Reached 5 minute timeout limit.");
                break;
            }

            var sw = Stopwatch.StartNew();
            runner.RunBenchmarkChunk(chunkSize);

            // Wait for GPU sync since the wrapper might only queue the launch
            Kassyi.GSharp.Runtime.Jit.JitEngine.SynchronizeDevice();
            sw.Stop();

            var throughput = mDigitsPerLoop * chunkSize / (sw.Elapsed.TotalSeconds);
            Console.WriteLine($"Iter {i}: Elapsed {sw.Elapsed.TotalMilliseconds:F2} ms, Throughput: {throughput:F2} M-Digit/s");
        }
    }

    [Command("bench", Description = "Run BenchmarkDotNet metrics for the Fermat Pipeline")]
    public void Bench()
    {
        Console.WriteLine("Starting BenchmarkDotNet...");
        BenchmarkRunner.Run<FermatBenchmarks>();
    }

    private static BigInteger GetBigIntegerFromResult(uint[] resultArray, BigInteger n)
    {
        var highestWord = resultArray[^1];
        var resBytes = new byte[resultArray.Length * 4 + 1];
        Buffer.BlockCopy(resultArray, 0, resBytes, 0, resultArray.Length * 4);
        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[resultArray.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;

        var actual = new BigInteger(resBytes);
        actual %= n;
        if (actual < 0)
        {
            actual += n;
        }

        return actual;
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class FermatBenchmarks
{
    [Params(1000, 5000, 10000)]
    public int E
    {
        get; set;
    }

    private BigInteger _n;
    private BigInteger _a;
    private BigInteger _exponent;
    private FermatRunner _gsharpRunner = null!;

    [GlobalSetup]
    public void Setup()
    {
        var x = BigInteger.One << E;
        _n = (BigInteger.One << (2 * E + 1)) - x + 11;
        _a = 2;
        _exponent = _n - 1;
        _gsharpRunner = new FermatRunner(E);
    }

    [Benchmark(Baseline = true)]
    public BigInteger StandardBigInteger()
    {
        if (E >= 5000)
        {
            return 0; // Skip to avoid huge CPU time
        }

        return BigInteger.ModPow(_a, _exponent, _n);
    }

    [Benchmark]
    public BigInteger GSharpJitPhase12()
    {
        if (E >= 5000)
        {
            return 0; // Skip to avoid NVRTC Timeout/TDR
        }

        var (resultArray, _) = _gsharpRunner.RunFermatPrp();

        var highestWord = resultArray[^1];
        var resBytes = new byte[resultArray.Length * 4 + 1];
        Buffer.BlockCopy(resultArray, 0, resBytes, 0, resultArray.Length * 4);

        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[resultArray.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;

        var actual = new BigInteger(resBytes);
        actual %= _n;
        if (actual < 0)
        {
            actual += _n;
        }

        return actual;
    }

    [Benchmark]
    public void GSharpJitChunk100()
    {
        if (E >= 5000)
        {
            return; // Skip to avoid NVRTC Timeout/TDR
        }

        _gsharpRunner.RunBenchmarkChunk();
    }
}

