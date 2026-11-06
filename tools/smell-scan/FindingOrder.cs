namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The order of a report: by path, then start line, start column, and rule id, all ordinal.</summary>
internal sealed class FindingOrder : IComparer<Finding>
{
    /// <summary>Gets the shared instance.</summary>
    public static FindingOrder Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(Finding? x, Finding? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byPath = string.CompareOrdinal(x.Measurement.Path, y.Measurement.Path);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = x.Measurement.Range.StartLine.CompareTo(y.Measurement.Range.StartLine);
        if (byLine != 0)
        {
            return byLine;
        }

        var byColumn = x.Measurement.Range.StartColumn.CompareTo(y.Measurement.Range.StartColumn);
        if (byColumn != 0)
        {
            return byColumn;
        }

        var byRule = string.CompareOrdinal(x.RuleId, y.RuleId);
        if (byRule != 0)
        {
            return byRule;
        }

        var byEnd = x.Measurement.Range.EndLine.CompareTo(y.Measurement.Range.EndLine);
        if (byEnd != 0)
        {
            return byEnd;
        }

        var byEndColumn = x.Measurement.Range.EndColumn.CompareTo(y.Measurement.Range.EndColumn);
        return byEndColumn != 0 ? byEndColumn : string.CompareOrdinal(x.Measurement.Metric, y.Measurement.Metric);
    }
}
