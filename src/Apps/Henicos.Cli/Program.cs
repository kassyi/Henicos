using System.Diagnostics;
using BenchmarkDotNet.Running;
using Cocona;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Cli;

/// <summary>Command-line interface entry point for executing Fermat tests, benchmarks, and profiling suites.</summary>
// ReSharper disable once UnusedMember.Global -- Used implicitly by CLI execution
// ReSharper disable once UnusedType.Global -- Used implicitly by CLI execution
internal class Program
{
    private static void Main(string[] args) => CoconaApp.Run<Program>(args);

    [Command("run", Description = "Run Fermat PRP test for a given Exponent (E)")]
    public void Run([Option('e', Description = "Fermat Exponent (E)")] int e = 1000)
    {
        Logger<Program>.Info($"Starting Fermat PRP test for E={e}...");
        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger a = 2;
        var exponent = n - 1;

        Logger<Program>.Info("Calculating expected result via CPU BigInteger...");
        var expected = BigInteger.ModPow(a, exponent, n);

        Logger<Program>.Info("Executing GPU GSharp Runner...");
        var runner = new FermatRunner(e);
        var (resultArray, _) = runner.RunFermatPrp();

        var actual = GetBigIntegerFromResult(resultArray, n);

        Logger<Program>.Info($"Expected: {expected}");
        Logger<Program>.Info($"Actual  : {actual}");
        Logger<Program>.Info($"Match?  : {expected == actual}");
    }

    [Command("profile", Description = "Profile the GSharp GPU execution and output M-Digit/s throughput")]
    public void Profile([Option('e', Description = "Fermat Exponent (E)")] int e = 10000,
                        [Option('i', Description = "Number of warmup/profile iterations")] int iterations = 2)
    {
        Logger<Program>.Info($"Profiling GSharp FermatRunner for E={e} (Iterations: {iterations})...");

        // Output for AI schedule hints
        var estimatedSeconds = 15 + (iterations * e / 500);
        Logger<Program>.Info($"Estimated max execution time: {estimatedSeconds} seconds.");

        var runner = new FermatRunner(e);

        var numBits = (2 * e + 1);
        var numDigits = numBits * Math.Log10(2);
        var mDigitsPerLoop = (numDigits * 2) / 1_000_000.0;

        var globalSw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (globalSw.Elapsed.TotalMinutes > 5)
            {
                Logger<Program>.Warning("Benchmark aborted: Reached 5 minute timeout limit.");
                break;
            }

            var sw = Stopwatch.StartNew();
            runner.RunFermatPrp();
            sw.Stop();

            var throughput = mDigitsPerLoop * e / (sw.Elapsed.TotalSeconds);
            Logger<Program>.Info($"Iter {i}: Elapsed {sw.ElapsedMilliseconds} ms, Throughput: {throughput:F2} M-Digit/s");
        }
    }

    [Command("profile-chunk", Description = "Profile the GSharp Chunk GPU execution to avoid TDR and get immediate M-Digit/s throughput")]
    public void ProfileChunk([Option('e', Description = "Fermat Exponent (E)")] int e = 10000,
                             [Option('i', Description = "Number of warmup/profile iterations")] int iterations = 2,
                             [Option('c', Description = "Chunk Size (Loop iterations in kernel)")] int chunkSize = 100)
    {
        Logger<Program>.Info($"Profiling GSharp Chunk Benchmark for E={e}, ChunkSize={chunkSize} (Iterations: {iterations})...");

        // Output for AI schedule hints
        var estimatedSeconds = 15 + (iterations * 2); // JIT comp (15s) + fast chunks (2s per iter)
        Logger<Program>.Info($"Estimated max execution time: {estimatedSeconds} seconds.");

        var runner = new FermatRunner(e);

        var numBits = (2 * e + 1);
        var numDigits = numBits * Math.Log10(2);
        var mDigitsPerLoop = (numDigits * 2) / 1_000_000.0;

        var globalSw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (globalSw.Elapsed.TotalMinutes > 5)
            {
                Logger<Program>.Warning("Benchmark aborted: Reached 5 minute timeout limit.");
                break;
            }

            var sw = Stopwatch.StartNew();
            runner.RunBenchmarkChunk(chunkSize);

            // Wait for GPU sync since the wrapper might only queue the launch
            GpuKernelLoader.SynchronizeDevice();
            sw.Stop();

            var throughput = mDigitsPerLoop * chunkSize / (sw.Elapsed.TotalSeconds);
            Logger<Program>.Info($"Iter {i}: Elapsed {sw.Elapsed.TotalMilliseconds:F2} ms, Throughput: {throughput:F2} M-Digit/s");
        }
    }

    [Command("bench", Description = "Run BenchmarkDotNet metrics for the Fermat Pipeline")]
    public void Bench()
    {
        Logger<Program>.Info("Starting BenchmarkDotNet...");
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
    public BigInteger GSharpAotPhase12()
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
    public void GSharpAotChunk100()
    {
        if (E >= 5000)
        {
            return; // Skip to avoid NVRTC Timeout/TDR
        }

        _gsharpRunner.RunBenchmarkChunk();
    }
}

