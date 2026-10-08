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
    private int _targetForShortCircuit;
    private int _endMarkerForWindowing;

    [Params(10_000, 1_000_000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new int[Size];
        for (var i = 0; i < Size; i++)
        {
            _data[i] = i;
        }

        // Zero out the first 10% for padding (used in Data Windowing)
        Array.Clear(_data, 0, Size / 10);

        _endMarkerForWindowing = _data[(int)(Size * 0.9)];
        _targetForShortCircuit = _data[(int)(Size * 0.1)];

        // Insert a sentinel target value at the center for Targeted Search
        _data[Size / 2] = 9999999;
    }

    // ==========================================
    // Scenario A: Heavy Pipeline (Filtering and Projection)
    // ==========================================

    [Benchmark]
    public int RawForLoop_HeavyPipeline()
    {
        var sum = 0;
        var span = _data.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            var x = span[i];
            if (x % 2 == 0)
            {
                sum += x * 3;
            }
        }

        return sum;
    }

    [Benchmark]
    public int SystemLinq_HeavyPipeline() => _data.Where(x => x % 2 == 0).Select(x => x * 3).Sum();

    [Benchmark]
    public int ZLinq_HeavyPipeline() => _data.AsValueEnumerable().Where(x => x % 2 == 0).Select(x => x * 3).Sum();

    [Benchmark]
    public int StackLinq_HeavyPipeline() => _data.AsStackEnumerable().Where(x => x % 2 == 0).Select(x => x * 3).Sum();

    // ==========================================
    // Scenario B: Short Circuit (Early Return)
    // ==========================================

    [Benchmark]
    public int RawForLoop_ShortCircuit()
    {
        var span = _data.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            var x = span[i];
            if (x == _targetForShortCircuit)
            {
                return x;
            }
        }

        throw new InvalidOperationException();
    }

    [Benchmark]
    public int SystemLinq_ShortCircuit() => _data.Where(x => x == _targetForShortCircuit).First();

    [Benchmark]
    public int ZLinq_ShortCircuit() => _data.AsValueEnumerable().Where(x => x == _targetForShortCircuit).First();

    [Benchmark]
    public int StackLinq_ShortCircuit() => _data.AsStackEnumerable().Where(x => x == _targetForShortCircuit).First();

    // ==========================================
    // Scenario C: Span Optimization (Boundary Slicing)
    // ==========================================

    [Benchmark]
    public int RawForLoop_SpanOptimization()
    {
        var sum = 0;
        var span = _data.AsSpan();
        var skip = Size / 10;
        var take = Size / 2;

        span = skip < span.Length ? span.Slice(skip) : default;

        if (take < span.Length)
        {
            span = span.Slice(0, take);
        }

        foreach (var t in span)
        {
            sum += t;
        }

        return sum;
    }

    [Benchmark]
    public int SystemLinq_SpanOptimization() => _data.Skip(Size / 10).Take(Size / 2).Sum();

    [Benchmark]
    public int ZLinq_SpanOptimization() => _data.AsValueEnumerable().Skip(Size / 10).Take(Size / 2).Sum();

    [Benchmark]
    public int StackLinq_SpanOptimization() => _data.AsStackEnumerable().Skip(Size / 10).Take(Size / 2).Sum();

    // ==========================================
    // Comprehensive Scenario 1: Data Windowing (Skip and Conditional Slicing)
    // ==========================================

    [Benchmark]
    public int SystemLinq_DataWindowing() =>
        _data.SkipLast(1).SkipWhile(x => x == 0).TakeWhile(x => x != _endMarkerForWindowing).Sum();

    [Benchmark]
    public int StackLinq_DataWindowing() =>
        _data.AsStackEnumerable().SkipLast(1).SkipWhile(x => x == 0).TakeWhile(x => x != _endMarkerForWindowing).Sum();

    // ==========================================
    // Comprehensive Scenario 2: Targeted Search (Multi-condition Pinpointing)
    // ==========================================

    [Benchmark]
    public int SystemLinq_TargetedSearch()
    {
        if (_data.Contains(-1))
        {
            return -1;
        }

        return _data.Where(x => x == 9999999).Single();
    }

    [Benchmark]
    public int StackLinq_TargetedSearch()
    {
        if (_data.AsStackEnumerable().Contains(-1))
        {
            return -1;
        }

        return _data.AsStackEnumerable().Where(x => x == 9999999).Single();
    }

    // ==========================================
    // Comprehensive Scenario 3: Boundary Span Optimization (Chained Slicing Optimization)
    // ==========================================

    [Benchmark]
    public int SystemLinq_BoundarySpanOpt() =>
        _data.TakeLast(Size / 2).Skip(10).Take(Size / 4).Sum();

    [Benchmark]
    public int StackLinq_BoundarySpanOpt() =>
        _data.AsStackEnumerable().TakeLast(Size / 2).Skip(10).Take(Size / 4).Sum();
}
