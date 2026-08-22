using System.Numerics;
using BenchmarkDotNet.Attributes;

namespace Henicos;

[MarkdownExporterAttribute.GitHub]
public class FermatBenchmark
{
    [Params(10, 50, 100, 500, 1000, 2000, 5000, 10000)]
    public int E
    {
        get; set;
    }

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
    }

    [Benchmark(Baseline = true)]
    public BigInteger StandardBigInteger() => BigInteger.ModPow(a, exponent, N);
}
