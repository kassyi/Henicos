using System.Diagnostics;
using BenchmarkDotNet.Running;
using Cocona;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Cli;

/// <summary>Command-line interface entry point for executing Fermat tests, benchmarks, and profiling suites.</summary>
// ReSharper disable once UnusedMember.Global -- Used implicitly by CLI execution
// ReSharper disable once UnusedType.Global -- Used implicitly by CLI execution
internal class Program
{
    private static void Main(string[] args) => CoconaApp.Run<Program>(args);

    [Command("run", Description = "Run the Fermat PRP test of N = 2^(2E+1) - 2^E + 11 and compare it with BigInteger")]
    public void Run([Option('e', Description = "Exponent E of N = 2^(2E+1) - 2^E + 11")] int e = 1001,
        [Option('a', Description = "Base of the Fermat test")] uint a = 3)
    {
        Logger<Program>.Info($"Starting the Fermat PRP test of N(E={e}) to base {a}...");
        using var runner = new FermatAlgorithm(e, a);
        var n = runner.GetModulus();

        Logger<Program>.Info("Calculating the expected verdict via CPU BigInteger (a^(N-1) mod N)...");
        var expected = BigInteger.ModPow(a, n - 1, n).IsOne;

        Logger<Program>.Info("Executing GPU GSharp Runner...");
        var result = runner.Run();

        Logger<Program>.Info($"Probable prime: {result.IsProbablePrime} (small divisor: {result.SmallDivisor?.ToString() ?? "none"})");
        Logger<Program>.Info($"Residue64     : {result.Residue64:X16}, check {result.CheckResidue64:X16}");
        Logger<Program>.Info($"Checks passed : {result.ChecksPassed}, rollbacks {result.Rollbacks}, {result.Iterations} squarings in {result.Elapsed}");
        Logger<Program>.Info($"Match?        : {expected == result.IsProbablePrime}");
    }

    [Command("profile", Description = "Profile the GSharp GPU execution and output M-Digit/s throughput")]
    public void Profile([Option('e', Description = "Fermat Exponent (E)")] int e = 10000,
        [Option('i', Description = "Number of warmup/profile iterations")]
        int iterations = 2)
    {
        Logger<Program>.Info($"Profiling GSharp FermatAlgorithm for E={e} (Iterations: {iterations})...");

        // Output for AI schedule hints
        var estimatedSeconds = 15 + (iterations * e / 500);
        Logger<Program>.Info($"Estimated max execution time: {estimatedSeconds} seconds.");

        // Base 2: for even E, the default base 3 divides N and the test ends without squaring.
        using var runner = new FermatAlgorithm(e, 2);

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
            var result = runner.Run();
            sw.Stop();

            var throughput = mDigitsPerLoop * result.Iterations / (sw.Elapsed.TotalSeconds);
            Logger<Program>.Info(
                $"Iter {i}: Elapsed {sw.ElapsedMilliseconds} ms, Throughput: {throughput:F2} M-Digit/s");
        }
    }

    [Command("profile-chunk",
        Description = "Profile the GSharp Chunk GPU execution to avoid TDR and get immediate M-Digit/s throughput")]
    public void ProfileChunk([Option('e', Description = "Fermat Exponent (E)")] int e = 10000,
        [Option('i', Description = "Number of warmup/profile iterations")]
        int iterations = 2,
        [Option('c', Description = "Chunk Size (Loop iterations in kernel)")]
        int chunkSize = 100)
    {
        Logger<Program>.Info(
            $"Profiling GSharp Chunk Benchmark for E={e}, ChunkSize={chunkSize} (Iterations: {iterations})...");

        // Output for AI schedule hints
        var estimatedSeconds = 15 + (iterations * 2); // Module loading + fast chunks (2s per iter)
        Logger<Program>.Info($"Estimated max execution time: {estimatedSeconds} seconds.");

        using var runner = new FermatAlgorithm(e, 2);

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

            GpuKernelLoader.SynchronizeDevice();
            sw.Stop();

            var throughput = mDigitsPerLoop * chunkSize / (sw.Elapsed.TotalSeconds);
            Logger<Program>.Info(
                $"Iter {i}: Elapsed {sw.Elapsed.TotalMilliseconds:F2} ms, Throughput: {throughput:F2} M-Digit/s");
        }
    }

    [Command("bench", Description = "Run BenchmarkDotNet metrics for the Fermat Pipeline")]
    public void Bench()
    {
        Logger<Program>.Info("Starting BenchmarkDotNet...");
        BenchmarkRunner.Run<FermatBenchmarks>();
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
    private FermatAlgorithm? _gsharpRunner;
    private FermatAlgorithm GsharpRunner => _gsharpRunner ?? throw new InvalidOperationException("FermatAlgorithm runner has not been initialized. Call Setup() first.");
    private nint _vramBefore;

    [GlobalSetup]
    public void Setup()
    {
        var x = BigInteger.One << E;
        _n = (BigInteger.One << (2 * E + 1)) - x + 11;
        _a = 2;
        _exponent = _n - 1;
        // Base 2 (as the BigInteger baseline): for even E, base 3 divides N and the test ends without squaring.
        _gsharpRunner = new FermatAlgorithm(E, 2);
    }

    [IterationSetup]
    public void IterationSetup() => CudaDriverApi.cuMemGetInfo_v2(out _vramBefore, out _);

    [IterationCleanup]
    public void IterationCleanup()
    {
        CudaDriverApi.cuMemGetInfo_v2(out var vramAfter, out _);
        long leakedMb = (_vramBefore - vramAfter) / 1024 / 1024;
        if (leakedMb > 0)
        {
            Console.WriteLine($"[VRAM LEAK] {leakedMb} MB leaked during iteration.");
        }
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
    public bool GSharpKernelPhase12()
    {
        if (E >= 5000)
        {
            return false; // Skip to avoid GPU Timeout/TDR
        }

        return GsharpRunner.Run().IsProbablePrime;
    }

    [Benchmark]
    public void GSharpKernelChunk100()
    {
        if (E >= 5000)
        {
            return; // Skip to avoid GPU Timeout/TDR
        }

        GsharpRunner.RunBenchmarkChunk();
    }
}

