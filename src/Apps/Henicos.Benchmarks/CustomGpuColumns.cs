using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

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
        if (stats == null) return "N/A";

        var meanNs = stats.Mean;
        if (meanNs <= 0) return "N/A";

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
