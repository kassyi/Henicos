
namespace Henicos.Cli;

/// <summary>BenchmarkDotNet performance comparisons for Fermat PRP algorithms on CPU vs GPU.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class FermatBenchmark
{
    [Params(1000)]
    public int E { get; set; }

    private FermatRunner _runner = null!;

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

        _runner = new FermatRunner(E);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemGetInfo_v2(out _vramBefore, out _);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemGetInfo_v2(out nint vramAfter, out _);
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
        var (result, _) = _runner.RunFermatPrp();
        return result;
    }

    [Benchmark]
    public void GSharpAotChunk100() => _runner.RunBenchmarkChunk();
}

