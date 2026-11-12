using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Henicos.Benchmarks;

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
