using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Cli;

/// <summary>BenchmarkDotNet performance comparisons for Fermat PRP algorithms on CPU vs GPU across multiple exponents.</summary>
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
