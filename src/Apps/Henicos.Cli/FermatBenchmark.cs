using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Cli;

/// <summary>BenchmarkDotNet performance comparisons for Fermat PRP algorithms on CPU vs GPU.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class FermatBenchmark
{
    [Params(1000)]
    public int E
    {
        get; set;
    }

    private FermatAlgorithm? _runner;
    private FermatAlgorithm Runner => _runner ?? throw new InvalidOperationException("Runner has not been initialized. Call Setup() first.");

    private BigInteger _baseValueX;
    private BigInteger _modulusN;
    private BigInteger _baseA;
    private BigInteger _exponentValue;
    private nint _vramBefore;

    [GlobalSetup]
    public void Setup()
    {
        _baseValueX = BigInteger.One << E;
        _modulusN = (BigInteger.One << (2 * E + 1)) - _baseValueX + 11;
        _baseA = 2;
        _exponentValue = _modulusN - 1;

        _runner = new FermatAlgorithm(E);
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
    public BigInteger StandardBigInteger() => BigInteger.ModPow(_baseA, _exponentValue, _modulusN);

    [Benchmark]
    public uint[] GpuBigIntJit()
    {
        var (result, _) = Runner.RunFermatPrp();
        return result;
    }

    [Benchmark]
    public void GSharpAotChunk100() => Runner.RunBenchmarkChunk();
}

