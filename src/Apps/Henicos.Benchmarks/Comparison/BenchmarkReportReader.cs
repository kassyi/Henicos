using System.Text.Json;

namespace Henicos.Benchmarks.Comparison;

/// <summary>
/// Reads the benchmarks and their measurements from the BenchmarkDotNet full JSON reports of one run.
/// </summary>
public static class BenchmarkReportReader
{
    private const string FullReportSuffix = "-report-full.json";
    private const string CompressedReportSuffix = "-report-full-compressed.json";

    /// <summary>
    /// Reads every benchmark of the reports at <paramref name="path"/>, keyed by <see cref="BenchmarkRun.Key"/>.
    /// </summary>
    /// <param name="path">A report file, or a directory searched recursively for reports.</param>
    /// <param name="side">The side the path belongs to (<c>base</c> or <c>head</c>), for messages.</param>
    /// <exception cref="BenchmarkReportException">
    /// The path holds no report or no benchmark, a report is malformed, a benchmark has no actual workload
    /// measurements, or the same benchmark and job appear twice.
    /// </exception>
    public static Dictionary<string, BenchmarkRun> Read(string path, string side)
    {
        var runs = new Dictionary<string, BenchmarkRun>(StringComparer.Ordinal);
        foreach (var file in FindReportFiles(path, side))
        {
            ReadFile(file, side, runs);
        }

        if (runs.Count == 0)
        {
            throw new BenchmarkReportException($"The {side} reports under {path} hold no benchmark.");
        }

        return runs;
    }

    /// <summary>
    /// Lists the <c>*-report-full.json</c> files at <paramref name="path"/>, and each
    /// <c>*-report-full-compressed.json</c> that has no uncompressed report of the same run beside it.
    /// </summary>
    /// <remarks>
    /// A run that exports both <c>JsonExporter.Full</c> and <c>JsonExporter.FullCompressed</c> holds the same
    /// benchmarks twice; only its uncompressed report is read.
    /// </remarks>
    public static List<string> FindReportFiles(string path, string side)
    {
        if (File.Exists(path))
        {
            return [Path.GetFullPath(path)];
        }

        if (!Directory.Exists(path))
        {
            throw new BenchmarkReportException($"The {side} path does not exist: {path}");
        }

        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories))
        {
            if (file.EndsWith(FullReportSuffix, StringComparison.OrdinalIgnoreCase) ||
                (file.EndsWith(CompressedReportSuffix, StringComparison.OrdinalIgnoreCase) &&
                 !File.Exists(file[..^CompressedReportSuffix.Length] + FullReportSuffix)))
            {
                files.Add(Path.GetFullPath(file));
            }
        }

        if (files.Count == 0)
        {
            throw new BenchmarkReportException(
                $"No BenchmarkDotNet *{FullReportSuffix} report found for {side} under {path}.");
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// The key of a benchmark: <c>&lt;namespace&gt;.&lt;type&gt;.&lt;method&gt;[&lt;parameters&gt;] @ &lt;job&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The job is the part of <c>DisplayInfo</c> after the method, with an id that BenchmarkDotNet generated
    /// (<c>Job-ABCDEF</c>) replaced by <c>Job</c>, so that the key rests on the job's characteristics, which are the
    /// same in a base and a head run of the same configuration.
    /// </remarks>
    public static string GetKey(JsonElement benchmark)
    {
        var job = GetJobDisplayInfo(benchmark);
        if (job.StartsWith("Job-", StringComparison.Ordinal))
        {
            var end = job.IndexOf('(');
            var idEnd = end < 0 ? job.Length : end;
            if (idEnd > "Job-".Length && IsAsciiLettersOrDigits(job.AsSpan("Job-".Length, idEnd - "Job-".Length)))
            {
                job = "Job" + job[idEnd..];
            }
        }

        var jobSuffix = job.Length > 0 ? " @ " + job : "";
        var type = GetString(benchmark, "Type");
        var method = GetMethod(benchmark);
        if (type.Length == 0 && method.Length == 0)
        {
            var fullName = GetString(benchmark, "FullName");
            return (fullName.Length > 0 ? fullName : "UnknownBenchmark") + jobSuffix;
        }

        var ns = GetString(benchmark, "Namespace");
        return (ns.Length > 0 ? ns + "." : "") + type + "." + method + GetParameterSuffix(benchmark) + jobSuffix;
    }

    /// <summary>
    /// The time of one operation in nanoseconds per actual workload iteration; the statistics' original values when
    /// the report has no measurements.
    /// </summary>
    public static double[] GetSamples(JsonElement benchmark)
    {
        var samples = new List<double>();
        if (benchmark.TryGetProperty("Measurements", out var measurements) &&
            measurements.ValueKind == JsonValueKind.Array)
        {
            foreach (var measurement in measurements.EnumerateArray())
            {
                if (GetString(measurement, "IterationMode") != "Workload" ||
                    GetString(measurement, "IterationStage") != "Actual" ||
                    !TryGetDouble(measurement, "Nanoseconds", out var nanoseconds) ||
                    !double.IsFinite(nanoseconds) ||
                    nanoseconds < 0)
                {
                    continue;
                }

                var operations = TryGetDouble(measurement, "Operations", out var count) && count > 0 ? count : 1;
                samples.Add(nanoseconds / operations);
            }
        }

        if (samples.Count == 0 &&
            benchmark.TryGetProperty("Statistics", out var statistics) &&
            statistics.ValueKind == JsonValueKind.Object &&
            statistics.TryGetProperty("OriginalValues", out var values) &&
            values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number && double.IsFinite(value.GetDouble()))
                {
                    samples.Add(value.GetDouble());
                }
            }
        }

        return [.. samples];
    }

    private static void ReadFile(string file, string side, Dictionary<string, BenchmarkRun> runs)
    {
        JsonDocument document;
        try
        {
            using var stream = File.OpenRead(file);
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException exception)
        {
            throw new BenchmarkReportException($"Failed to parse the {side} report {file}: {exception.Message}",
                exception);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("Benchmarks", out var benchmarks) ||
                benchmarks.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var benchmark in benchmarks.EnumerateArray())
            {
                var key = GetKey(benchmark);
                var samples = GetSamples(benchmark);
                if (samples.Length == 0)
                {
                    throw new BenchmarkReportException(
                        $"Benchmark '{key}' in the {side} report {file} has no actual workload measurements. " +
                        "Export the run with JsonExporter.Full.");
                }

                if (runs.TryGetValue(key, out var existing))
                {
                    throw new BenchmarkReportException(
                        $"Benchmark '{key}' appears twice in {side}: in {existing.File} and in {file}. " +
                        "Pass one run per side (a single report file or a directory holding one run).");
                }

                runs.Add(key, new BenchmarkRun(key, GetDisplayName(benchmark), file, samples));
            }
        }
    }

    // <type>.<method>[<parameters>] (<job id>), as shown in the table
    private static string GetDisplayName(JsonElement benchmark)
    {
        var job = GetJobDisplayInfo(benchmark);
        var paren = job.IndexOf('(');
        var jobId = paren >= 0 ? job[..paren] : job;
        var jobSuffix = jobId.Length > 0 ? " (" + jobId + ")" : "";
        var type = GetString(benchmark, "Type");
        var method = GetMethod(benchmark);
        if (type.Length > 0 && method.Length > 0)
        {
            return type + "." + method + GetParameterSuffix(benchmark) + jobSuffix;
        }

        var fullName = GetString(benchmark, "FullName");
        return (fullName.Length > 0 ? fullName : GetKey(benchmark)) + jobSuffix;
    }

    // BenchmarkDotNet writes DisplayInfo as "<type>.<method>: <job id>(<characteristics>)", followed by
    // " [<parameters>]" when the benchmark has parameters. Returns the job part, or "" when there is none
    private static string GetJobDisplayInfo(JsonElement benchmark)
    {
        var info = GetString(benchmark, "DisplayInfo");
        var separator = info.IndexOf(": ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return "";
        }

        var job = info.AsSpan(separator + 2).Trim().ToString();
        if (GetString(benchmark, "Parameters").Length > 0 && job.EndsWith(']'))
        {
            var open = job.LastIndexOf('[');
            if (open > 0 && char.IsWhiteSpace(job[open - 1]) && job.IndexOf(']', open) == job.Length - 1)
            {
                job = job[..(open - 1)];
            }
        }

        return job;
    }

    private static string GetMethod(JsonElement benchmark)
    {
        var method = GetString(benchmark, "Method");
        return method.Length > 0 ? method : GetString(benchmark, "MethodTitle");
    }

    private static string GetParameterSuffix(JsonElement benchmark)
    {
        var parameters = GetString(benchmark, "Parameters");
        return parameters.Length > 0 ? "[" + parameters + "]" : "";
    }

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value);
    }

    private static bool IsAsciiLettersOrDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
