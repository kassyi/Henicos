using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Henicos.Benchmarks;

public class GpuTimeColumn : IColumn
{
    public string Id => "GpuTime";
    public string ColumnName => "Kernel Time (us)";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Metric;
    public int PriorityInCategory => 1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Time;
    public string Legend => "Pure GPU Kernel Execution Time in microseconds";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        if (GpuMetricsDiagnoser.Default.Results.TryGetValue(benchmarkCase, out var data) && data.GpuTimes.Count > 0)
        {
            return data.GpuTimes.AsStackEnumerable().Average().ToString("F1") + " us";
        }

        return "N/A";
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
        GetValue(summary, benchmarkCase);

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;
}
