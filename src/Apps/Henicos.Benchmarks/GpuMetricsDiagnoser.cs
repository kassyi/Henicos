using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace Henicos.Benchmarks;

public class GpuMetricsDiagnoser : IDiagnoser
{
    public static readonly GpuMetricsDiagnoser Default = new();

    public Dictionary<BenchmarkCase, (List<double> GpuTimes, double Flops, double Bytes)> Results { get; } = new();
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
