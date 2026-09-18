using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace Henicos.Benchmarks.Infrastructure;

public class GpuMetricsDiagnoser : IDiagnoser
{
    public static readonly GpuMetricsDiagnoser Default = new();

    public readonly Dictionary<BenchmarkCase, (List<double> GpuTimes, double Flops, double Bytes)> Results = new();
    private (List<double> GpuTimes, double Flops, double Bytes) _currentContext;

    public IEnumerable<string> Ids => [nameof(GpuMetricsDiagnoser)];
    public IEnumerable<IExporter> Exporters => [];
    public IEnumerable<IAnalyser> Analysers => [];

    public RunMode GetRunMode(BenchmarkCase benchmarkCase) => RunMode.NoOverhead;

    public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
    {
        switch (signal)
        {
            case HostSignal.BeforeAnythingElse:
                _currentContext = ([], 0, 0);
                break;

            case HostSignal.AfterActualRun:
                if (GpuMetricsContext.GetAndResetGpuTimeUs() is var time and > 0)
                {
                    _currentContext.GpuTimes.Add(time);
                }

                if (GpuMetricsContext.GetAndResetFlops() is var flops and > 0)
                {
                    _currentContext.Flops = flops;
                }

                if (GpuMetricsContext.GetAndResetBytes() is var bytes and > 0)
                {
                    _currentContext.Bytes = bytes;
                }

                break;

            case HostSignal.AfterAll:
                Results.TryAdd(parameters.BenchmarkCase, _currentContext);
                break;
        }
    }

    public IEnumerable<Metric> ProcessResults(DiagnoserResults results) => Array.Empty<Metric>();

    public void DisplayResults(ILogger logger)
    {
    }

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters) =>
        Array.Empty<ValidationError>();
}

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
            return data.GpuTimes.Average().ToString("F1") + " us";
        }

        return "N/A";
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
        GetValue(summary, benchmarkCase);

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;
}

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

        var ms = data.GpuTimes.Average() * 1000.0;

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
