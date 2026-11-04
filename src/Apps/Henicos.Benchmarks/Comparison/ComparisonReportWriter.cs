using System.Text.Json;

namespace Henicos.Benchmarks.Comparison;

/// <summary>Writes a <see cref="ComparisonReport"/> as a Markdown table or as JSON.</summary>
public static class ComparisonReportWriter
{
    private static readonly string[] s_headers =
        ["Benchmark", "N (B/H)", "Base Median", "Head Median", "Delta (%)", "U", "p-value", "Status"];

    // The benchmark name and the status are left-aligned; the numbers are right-aligned
    private static readonly bool[] s_rightAligned = [false, true, true, true, true, true, true, false];

    /// <summary>
    /// Writes the comparison table, the benchmarks found on one side only, and a summary line.
    /// </summary>
    public static void WriteTable(TextWriter output, ComparisonReport report, double thresholdPercent,
        double pValueThreshold)
    {
        var rows = new List<string[]>(report.Comparisons.Count);
        foreach (var comparison in report.Comparisons)
        {
            rows.Add(
            [
                comparison.Name,
                Format($"{comparison.Base.Count}/{comparison.Head.Count}"),
                FormatDuration(comparison.Base.Median),
                FormatDuration(comparison.Head.Median),
                Format($"{(comparison.MedianDeltaPercent >= 0 ? "+" : "")}{comparison.MedianDeltaPercent:F2}%"),
                Format($"{comparison.U:F1}"),
                comparison.PValue < 0.0001 ? "<0.0001" : Format($"{comparison.PValue:F4}"),
                comparison.Status.ToString()
            ]);
        }

        var widths = new int[s_headers.Length];
        for (var column = 0; column < s_headers.Length; column++)
        {
            widths[column] = s_headers[column].Length;
            foreach (var row in rows)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        output.WriteLine(Format(
            $"Mann-Whitney U benchmark comparison (threshold: >{thresholdPercent}% change, p < {pValueThreshold})"));
        output.WriteLine();
        WriteRow(output, s_headers, widths);
        output.Write('|');
        for (var column = 0; column < widths.Length; column++)
        {
            output.Write('-');
            output.Write(new string('-', widths[column]));
            output.Write(s_rightAligned[column] ? ":|" : "-|");
        }

        output.WriteLine();
        foreach (var row in rows)
        {
            WriteRow(output, row, widths);
        }

        WriteKeys(output, "Only in base (not compared):", report.OnlyInBase);
        WriteKeys(output, "Only in head (not compared):", report.OnlyInHead);

        var regressions = report.Count(ComparisonStatus.Regression);
        var improvements = report.Count(ComparisonStatus.Improvement);
        var unchanged = report.Count(ComparisonStatus.Unchanged);
        output.WriteLine();
        output.Write(Format($"Summary: {report.Comparisons.Count} compared, {regressions} regression(s), "));
        output.Write(Format($"{improvements} improvement(s), {unchanged} unchanged, "));
        output.WriteLine(Format($"{report.OnlyInBase.Count} only in base, {report.OnlyInHead.Count} only in head."));
    }

    /// <summary>Writes the thresholds, the summary, every comparison and the unmatched keys to a JSON file.</summary>
    public static void WriteJson(string file, ComparisonReport report, double thresholdPercent,
        double pValueThreshold)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(file);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("thresholdPercent", thresholdPercent);
        json.WriteNumber("pValueThreshold", pValueThreshold);

        json.WriteStartObject("summary");
        json.WriteNumber("compared", report.Comparisons.Count);
        json.WriteNumber("regressions", report.Count(ComparisonStatus.Regression));
        json.WriteNumber("improvements", report.Count(ComparisonStatus.Improvement));
        json.WriteNumber("unchanged", report.Count(ComparisonStatus.Unchanged));
        json.WriteNumber("onlyInBase", report.OnlyInBase.Count);
        json.WriteNumber("onlyInHead", report.OnlyInHead.Count);
        json.WriteEndObject();

        json.WriteStartArray("comparisons");
        foreach (var comparison in report.Comparisons)
        {
            json.WriteStartObject();
            json.WriteString("key", comparison.Key);
            json.WriteString("name", comparison.Name);
            WriteSummary(json, "base", comparison.Base);
            WriteSummary(json, "head", comparison.Head);
            json.WriteNumber("medianDeltaPercent", comparison.MedianDeltaPercent);
            json.WriteNumber("u", comparison.U);
            json.WriteNumber("pValue", comparison.PValue);
            json.WriteString("status", comparison.Status.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteStrings(json, "onlyInBase", report.OnlyInBase);
        WriteStrings(json, "onlyInHead", report.OnlyInHead);
        json.WriteEndObject();
    }

    private static void WriteRow(TextWriter output, string[] cells, int[] widths)
    {
        output.Write('|');
        for (var column = 0; column < cells.Length; column++)
        {
            output.Write(' ');
            output.Write(s_rightAligned[column]
                ? cells[column].PadLeft(widths[column])
                : cells[column].PadRight(widths[column]));
            output.Write(" |");
        }

        output.WriteLine();
    }

    private static void WriteKeys(TextWriter output, string title, List<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine(title);
        foreach (var key in keys)
        {
            output.WriteLine("  " + key);
        }
    }

    private static void WriteSummary(Utf8JsonWriter json, string name, SampleSummary summary)
    {
        json.WriteStartObject(name);
        json.WriteNumber("n", summary.Count);
        json.WriteNumber("median", summary.Median);
        json.WriteNumber("mean", summary.Mean);
        json.WriteNumber("min", summary.Min);
        json.WriteNumber("max", summary.Max);
        json.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter json, string name, List<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    private static string FormatDuration(double nanoseconds)
    {
        var abs = Math.Abs(nanoseconds);
        return abs switch
        {
            >= 1e9 => Format($"{nanoseconds / 1e9:F3} s"),
            >= 1e6 => Format($"{nanoseconds / 1e6:F2} ms"),
            >= 1e3 => Format($"{nanoseconds / 1e3:F2} us"),
            _ => Format($"{nanoseconds:F2} ns")
        };
    }

    private static string Format(FormattableString text) => FormattableString.Invariant(text);
}
