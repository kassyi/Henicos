using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Henicos.Benchmarks;

/// <summary>
/// BenchmarkDotNet configuration for CPU-only benchmarks (such as <see cref="GeneratorBenchmark"/>) that do not use the
/// GPU and export full per-iteration measurements (<c>*-report-full.json</c>) for statistical baseline comparison.
/// </summary>
public class CpuBenchmarkConfig : ManualConfig
{
    public CpuBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance));

        AddExporter(JsonExporter.Full);

        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
    }
}
