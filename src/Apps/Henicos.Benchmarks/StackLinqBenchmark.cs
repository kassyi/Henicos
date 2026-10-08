using BenchmarkDotNet.Attributes;
using Kassyi.ZeroAlloc.Linq;
using System.Linq;
using ZLinq;

#pragma warning disable RS0030 // System.Linq is banned in G# but required here for benchmark comparison

namespace Henicos.Benchmarks;

[MemoryDiagnoser]
public class StackLinqBenchmark
{
    private int[] _data = null!;

    [Params(10_000, 1_000_000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() => _data = [.. Enumerable.Range(0, Size)];

    [Benchmark(Baseline = true)]
    public int SystemLinq_WhereSelectSum()
    {
        return _data.Where(x => x % 2 == 0)
                    .Select(x => x * 2)
                    .Sum();
    }

    [Benchmark]
    public int StackLinq_WhereSelectSum()
    {
        return _data.AsStackEnumerable()
                    .Where(x => x % 2 == 0)
                    .Select(x => x * 2)
                    .Sum();
    }

    [Benchmark]
    public int ZLinq_WhereSelectSum()
    {
        return _data.AsValueEnumerable()
                    .Where(x => x % 2 == 0)
                    .Select(x => x * 2)
                    .Sum();
    }

    [Benchmark]
    public int SystemLinq_Count() => _data.Count(x => x > Size / 2);

    [Benchmark]
    public int StackLinq_Count() => _data.AsStackEnumerable().Count(x => x > Size / 2);

    [Benchmark]
    public int ZLinq_Count() => _data.AsValueEnumerable().Count(x => x > Size / 2);

    [Benchmark]
    public int SystemLinq_First() => _data.First(x => x == Size - 1);

    [Benchmark]
    public int StackLinq_First() => _data.AsStackEnumerable().First(x => x == Size - 1);

    [Benchmark]
    public int ZLinq_First() => _data.AsValueEnumerable().First(x => x == Size - 1);

    [Benchmark]
    public int[] SystemLinq_WhereToArray() => _data.Where(x => x % 2 == 0).ToArray();

    [Benchmark]
    public int[] StackLinq_WhereToArray() => _data.AsStackEnumerable().Where(x => x % 2 == 0).ToArray();

    [Benchmark]
    public int[] ZLinq_WhereToArray() => _data.AsValueEnumerable().Where(x => x % 2 == 0).ToArray();
}
