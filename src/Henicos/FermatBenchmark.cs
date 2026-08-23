using System.Numerics;
using BenchmarkDotNet.Attributes;

namespace Henicos;

[MarkdownExporterAttribute.GitHub]
public class FermatBenchmark
{
    [Params(1000, 5000, 10000)]
    public int E
    {
        get; set;
    }

    private Kassyi.GSharp.GpuBigInt.FermatRunner _runner;

    private BigInteger X;
    private BigInteger N;
    private BigInteger a;
    private BigInteger exponent;

    [GlobalSetup]
    public void Setup()
    {
        X = BigInteger.One << E;
        N = (BigInteger.One << (2 * E + 1)) - X + 11;
        a = 2;
        exponent = N - 1;

        _runner = new Kassyi.GSharp.GpuBigInt.FermatRunner(E);
    }

    [Benchmark(Baseline = true)]
    public BigInteger StandardBigInteger() => BigInteger.ModPow(a, exponent, N);

    [Benchmark]
    public uint[] GpuBigIntJit()
    {
        var (result, _) = _runner.RunFermatPrp();
        return result;
    }
}
