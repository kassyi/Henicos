using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Henicos.Benchmarks;

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        Add(DefaultConfig.Instance);

        // Add our custom GPU Metrics Diagnoser
        AddDiagnoser(GpuMetricsDiagnoser.Default);

        // We use InProcessEmitToolchain to avoid multi-process startup overhead 
        // and to keep the CUDA context alive during the benchmark run.
        var job = Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance);

        AddJob(job);

        // Add Markdown, CSV, and JSON (full per-iteration measurements) exporters for comparison
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);
        AddExporter(JsonExporter.Full);

        // Custom Columns (GpuMetricsDiagnoser automatically adds its custom columns)
        AddColumn(new GpuTimeColumn());
        AddColumn(new GpuMetricsScoreColumn());
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
    }
}

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
