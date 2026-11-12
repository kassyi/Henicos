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
            if (p is { Name: "NumWords", Value: int numWords })
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
