using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class BitsColumn : IColumn
{
    public string Id => nameof(BitsColumn);
    public string ColumnName => "Bits";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Params;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Bit width of the operands";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        foreach (var p in benchmarkCase.Parameters.Items)
        {
            if (p.Name == "NumWords" && p.Value is int numWords)
            {
                return (numWords * 32).ToString();
            }
        }
        return "?";
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
}

public class OpsPerSecColumn : IColumn
{
    public string Id => nameof(OpsPerSecColumn);
    public string ColumnName => "Ops/sec (Throughput)";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Operations per second (Throughput)";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        var stats = summary[benchmarkCase]?.ResultStatistics;
        if (stats == null)
        {
            return "N/A";
        }

        var meanNs = stats.Mean;
        if (meanNs <= 0)
        {
            return "N/A";
        }

        foreach (var p in benchmarkCase.Parameters.Items)
        {
            if (p.Name == "BatchSize" && p.Value is int batchSize)
            {
                var seconds = meanNs / 1_000_000_000.0;
                var opsPerSec = batchSize / seconds;
                return opsPerSec.ToString("N0");
            }
        }
        
        return "N/A";
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
}

public class KernelRegistersColumn : IColumn
{
    public string Id => nameof(KernelRegistersColumn);
    public string ColumnName => "Regs (G#/Nat)";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 2;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Hardware registers allocated per thread (G# vs Native CUDA)";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        => BenchmarkKernelResourceHelper.GetOrInspectResources(benchmarkCase.Descriptor.Type).Regs;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
}

public class KernelSpillMemoryColumn : IColumn
{
    public string Id => nameof(KernelSpillMemoryColumn);
    public string ColumnName => "Spill B (G#/Nat)";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 3;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Local memory spill bytes per thread (G# vs Native CUDA)";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        => BenchmarkKernelResourceHelper.GetOrInspectResources(benchmarkCase.Descriptor.Type).Spill;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
}

internal static class BenchmarkKernelResourceHelper
{
    private static readonly Dictionary<Type, Func<IComputeBenchmark>> s_typeFactories = new()
    {
        { typeof(SgemmBenchmark), () => new SgemmBenchmark() },
        { typeof(Sha256Benchmark), () => new Sha256Benchmark() },
        { typeof(BlackScholesBenchmark), () => new BlackScholesBenchmark() },
        { typeof(ReductionBenchmark), () => new ReductionBenchmark() }
    };

    private static readonly Dictionary<Type, (string Regs, string Spill)> s_cache = new();
    private static readonly Lock s_lock = new();

    public static (string Regs, string Spill) GetOrInspectResources(Type targetType)
    {
        lock (s_lock)
        {
            if (s_cache.TryGetValue(targetType, out var cached))
            {
                return cached;
            }

            if (!s_typeFactories.TryGetValue(targetType, out var factory))
            {
                return ("-", "-");
            }

            try
            {
                var benchmark = factory();
                benchmark.Initialize();
                var gsharpAttrs = KernelResourceInspector.TryGetAttributes(benchmark.GetGSharpFunction());
                var nativeAttrs = KernelResourceInspector.TryGetAttributes(benchmark.GetNativeFunction());

                var gsharpRegs = gsharpAttrs?.NumRegs.ToString() ?? "-";
                var nativeRegs = nativeAttrs?.NumRegs.ToString() ?? "-";
                var regs = $"{gsharpRegs} / {nativeRegs}";

                var gsharpSpill = gsharpAttrs?.LocalSizeBytes.ToString() ?? "-";
                var nativeSpill = nativeAttrs?.LocalSizeBytes.ToString() ?? "-";
                var spill = $"{gsharpSpill} / {nativeSpill}";

                var result = (regs, spill);
                s_cache[targetType] = result;
                return result;
            }
            catch
            {
                // Ignore initialization failures in column inspection
                s_cache[targetType] = ("-", "-");
                return ("-", "-");
            }
        }
    }
}

