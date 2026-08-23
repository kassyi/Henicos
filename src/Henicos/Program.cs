using System.Diagnostics;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Kassyi.GSharp.GpuBigInt;

namespace Henicos;

internal class Program
{
    private    static void Main(string[] args)
    {
        Console.WriteLine("Validating Fermat logic for E=1000...");
        int e = 1000;
        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger a = 2;
        var exponent = n - 1;
        var expected = BigInteger.ModPow(a, exponent, n);

        var runner = new FermatRunner(e);
        var (resultArray, _) = runner.RunFermatPrp();
        
        var highestWord = resultArray[^1];
        var resBytes = new byte[resultArray.Length * 4 + 1];
        Buffer.BlockCopy(resultArray, 0, resBytes, 0, resultArray.Length * 4);
        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[resultArray.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;
        
        var actual = new BigInteger(resBytes);
        actual %= n;
        if (actual < 0) actual += n;

        Console.WriteLine($"Expected: {expected}");
        Console.WriteLine($"Actual  : {actual}");
        Console.WriteLine($"Match?  : {expected == actual}");

        if (expected == actual)
        {
            Console.WriteLine("Validation passed! Starting Benchmark...");
            if (args.Length > 0 && args[0] == "--run-benchmark") {
                BenchmarkRunner.Run<FermatBenchmarks>();
            }
        }
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class FermatBenchmarks
{
    [Params(1000, 5000, 10000)]
    public int E { get; set; }

    private BigInteger _n;
    private BigInteger _a;
    private BigInteger _exponent;

    private FermatRunner _gsharpRunner = null!;

    // For Hybrid CPU
    private uint[] _hybridData = null!;
    private int _hybridNumWords;

    [GlobalSetup]
    public void Setup()
    {
        var x = BigInteger.One << E;
        _n = (BigInteger.One << (2 * E + 1)) - x + 11;
        _a = 2;
        _exponent = _n - 1;

        // Initialize GSharp JIT Runner (This avoids compilation overhead during benchmarking)
        _gsharpRunner = new FermatRunner(E);

        // Setup Hybrid Data
        _hybridNumWords = (2 * E + 1 + 31) / 32;
        var t = BigInteger.Pow(x, 2) + 123456789;
        var tBytes = t.ToByteArray();
        _hybridData = new uint[_hybridNumWords * 4];
        Buffer.BlockCopy(tBytes, 0, _hybridData, 0, tBytes.Length);
    }

    [Benchmark(Baseline = true)]
    public BigInteger StandardBigInteger() => BigInteger.ModPow(_a, _exponent, _n);

    [Benchmark]
    public BigInteger GSharpJitPhase12()
    {
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
    public void HybridCpuV3_ReduceOnly() => HybridReduction.ReduceInPlace(_hybridData, E, _hybridNumWords);
}
