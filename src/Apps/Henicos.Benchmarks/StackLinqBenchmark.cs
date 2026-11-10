using System.Linq;
using ZLinq;

#pragma warning disable RS0030 // System.Linq is banned in G# but required here for benchmark comparison

namespace Henicos.Benchmarks;

[MemoryDiagnoser]
public class StackLinqBenchmark
{
    private int[] _data = [];
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

    [Benchmark(Description = "RawForLoop_HeavyPipeline")]
    public int RawForLoopHeavyPipeline()
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

    [Benchmark(Description = "SystemLinq_HeavyPipeline")]
    public int SystemLinqHeavyPipeline() => _data.Where(x => x % 2 == 0).Select(x => x * 3).Sum();

    [Benchmark(Description = "ZLinq_HeavyPipeline")]
    public int ZLinqHeavyPipeline() => _data.AsValueEnumerable().Where(x => x % 2 == 0).Select(x => x * 3).Sum();

    [Benchmark(Description = "StackLinq_HeavyPipeline")]
    public int StackLinqHeavyPipeline() => _data.AsStackEnumerable().Where(x => x % 2 == 0).Select(x => x * 3).Sum();

    // ==========================================
    // Scenario B: Short Circuit (Early Return)
    // ==========================================

    [Benchmark(Description = "RawForLoop_ShortCircuit")]
    public int RawForLoopShortCircuit()
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

    [Benchmark(Description = "SystemLinq_ShortCircuit")]
    public int SystemLinqShortCircuit() => _data.Where(x => x == _targetForShortCircuit).First();

    [Benchmark(Description = "ZLinq_ShortCircuit")]
    public int ZLinqShortCircuit() => _data.AsValueEnumerable().Where(x => x == _targetForShortCircuit).First();

    [Benchmark(Description = "StackLinq_ShortCircuit")]
    public int StackLinqShortCircuit() => _data.AsStackEnumerable().Where(x => x == _targetForShortCircuit).First();

    // ==========================================
    // Scenario C: Span Optimization (Boundary Slicing)
    // ==========================================

    [Benchmark(Description = "RawForLoop_SpanOptimization")]
    public int RawForLoopSpanOptimization()
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

    [Benchmark(Description = "SystemLinq_SpanOptimization")]
    public int SystemLinqSpanOptimization() => _data.Skip(Size / 10).Take(Size / 2).Sum();

    [Benchmark(Description = "ZLinq_SpanOptimization")]
    public int ZLinqSpanOptimization() => _data.AsValueEnumerable().Skip(Size / 10).Take(Size / 2).Sum();

    [Benchmark(Description = "StackLinq_SpanOptimization")]
    public int StackLinqSpanOptimization() => _data.AsStackEnumerable().Skip(Size / 10).Take(Size / 2).Sum();

    // ==========================================
    // Comprehensive Scenario 1: Data Windowing (Skip and Conditional Slicing)
    // ==========================================

    [Benchmark(Description = "SystemLinq_DataWindowing")]
    public int SystemLinqDataWindowing() =>
        _data.SkipLast(1).SkipWhile(x => x == 0).TakeWhile(x => x != _endMarkerForWindowing).Sum();

    [Benchmark(Description = "StackLinq_DataWindowing")]
    public int StackLinqDataWindowing() =>
        _data.AsStackEnumerable().SkipLast(1).SkipWhile(x => x == 0).TakeWhile(x => x != _endMarkerForWindowing).Sum();

    // ==========================================
    // Comprehensive Scenario 2: Targeted Search (Multi-condition Pinpointing)
    // ==========================================

    [Benchmark(Description = "SystemLinq_TargetedSearch")]
    public int SystemLinqTargetedSearch()
    {
        if (_data.Contains(-1))
        {
            return -1;
        }

        return _data.Where(x => x == 9999999).Single();
    }

    [Benchmark(Description = "StackLinq_TargetedSearch")]
    public int StackLinqTargetedSearch()
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

    [Benchmark(Description = "SystemLinq_BoundarySpanOpt")]
    public int SystemLinqBoundarySpanOpt() =>
        _data.TakeLast(Size / 2).Skip(10).Take(Size / 4).Sum();

    [Benchmark(Description = "StackLinq_BoundarySpanOpt")]
    public int StackLinqBoundarySpanOpt() =>
        _data.AsStackEnumerable().TakeLast(Size / 2).Skip(10).Take(Size / 4).Sum();
}
