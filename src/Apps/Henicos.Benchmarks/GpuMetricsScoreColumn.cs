using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Henicos.Benchmarks;

public class GpuMetricsScoreColumn : IColumn
{
    public string Id => "GpuMetricsScore";
    public string ColumnName => "Metrics (Score)";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Metric;
    public int PriorityInCategory => 2;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Throughput Score (GFLOPS or GB/s)";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        if (!GpuMetricsDiagnoser.Default.Results.TryGetValue(benchmarkCase, out var data) || data.GpuTimes.Count == 0)
        {
            return "N/A";
        }

        var ms = data.GpuTimes.AsStackEnumerable().Average() * 1000.0;

        return data switch
        {
            { Flops: > 0 } => $"{data.Flops / ms:F2} GFLOPS",
            { Bytes: > 0 } => $"{data.Bytes / ms:F2} GB/s",
            _ => "N/A"
        };
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
        GetValue(summary, benchmarkCase);

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;
}
