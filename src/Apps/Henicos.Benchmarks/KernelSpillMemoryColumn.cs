using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Henicos.Benchmarks;

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
