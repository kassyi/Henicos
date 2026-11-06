namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The words that tell a reader what a measured number is. Writers use it; it decides nothing.</summary>
internal static class MetricText
{
    /// <summary>Describes a measurement, such as <c>141 code lines (limit 60)</c>.</summary>
    public static string Describe(Measurement measurement, int threshold) => measurement.Metric switch
    {
        "codeLines" => $"{measurement.Value} code lines (limit {threshold})",
        "parameters" => $"{measurement.Value} parameters (limit {threshold})",
        "depth" => $"nesting depth {measurement.Value} (limit {threshold})",
        "members" => $"{measurement.Value} members (limit {threshold})",
        "cases" => $"{measurement.Value} switch cases (limit {threshold})",
        "branches" => $"{measurement.Value} branches in one if chain (limit {threshold})",
        "columns" => $"a line of {measurement.Value} columns holds a string literal (limit {threshold})",
        "rawContentColumns" => $"a line of {measurement.Value} columns in a multi-line raw string (limit {threshold})",
        "emptyCatch" => "a catch clause with an empty handler",
        "logOnlyCatch" => "a catch clause whose handler only writes a log line",
        _ => $"{measurement.Value} {measurement.Metric} (limit {threshold})",
    };

    /// <summary>Gets the short unit label of a metric, such as <c>lines</c>, for aligned listings.</summary>
    public static string Unit(string metric) => metric switch
    {
        "codeLines" => "lines",
        "parameters" => "params",
        "depth" => "depth",
        "members" => "members",
        "cases" => "cases",
        "branches" => "branches",
        "columns" => "cols",
        "rawContentColumns" => "cols(raw)",
        "emptyCatch" => "empty",
        "logOnlyCatch" => "log-only",
        _ => metric,
    };

    /// <summary>Gets the sentence of a finding: what was measured, where, and the advice of the rule.</summary>
    public static string Message(Finding finding)
    {
        var what = Describe(finding.Measurement, finding.Threshold);
        var symbol = finding.Measurement.Symbol;
        var head = symbol.Length == 0 ? Capitalize(what) : $"{symbol}: {what}";
        return finding.Suggestion.Length == 0 ? head + "." : $"{head}. {finding.Suggestion}";
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
