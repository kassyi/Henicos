using System.Numerics;
using BenchmarkDotNet.Attributes;
using Kassyi.GSharp.Stdlib.Math;

namespace Henicos.Cli;

[MarkdownExporterAttribute.GitHub]
public class FermatBenchmark
{
    [Params(1000, 5000, 10000)]
    public int E
    {
        get; set;
    }

    private FermatRunner _runner = null!;

    private BigInteger _baseValueX;
    private BigInteger _modulusN;
    private BigInteger _baseA;
    private BigInteger _exponentValue;

    [GlobalSetup]
    public void Setup()
    {
        _baseValueX = BigInteger.One << E;
        _modulusN = (BigInteger.One << (2 * E + 1)) - _baseValueX + 11;
        _baseA = 2;
        _exponentValue = _modulusN - 1;

        _runner = new FermatRunner(E);
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
    public void GSharpJitChunk100() => _runner.RunBenchmarkChunk();
}
