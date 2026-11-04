using System.Globalization;

namespace Henicos.Benchmarks.Comparison;

/// <summary>
/// The <c>bench-compare &lt;base&gt; &lt;head&gt;</c> command: compares the BenchmarkDotNet full JSON reports of a base
/// and a head run per benchmark and job, and fails on a significant slowdown.
/// </summary>
public static class BenchCompareCommand
{
    /// <summary>No benchmark regressed.</summary>
    public const int ExitSuccess = 0;

    /// <summary>At least one benchmark regressed.</summary>
    public const int ExitRegression = 1;

    /// <summary>Invalid arguments, unreadable reports, or nothing to compare.</summary>
    public const int ExitInvalidInput = 2;

    private const double DefaultThresholdPercent = 5.0;
    private const double DefaultPValue = 0.01;

    private const string Usage = """
        Usage: bench-compare <base> <head> [--threshold <percent>] [--pvalue <p>] [--json-out <file>]

          <base>, <head>          A BenchmarkDotNet *-report-full.json file, or a directory searched recursively.
                                  Each side must hold one run: the same benchmark and job twice on one side is an error.
          --threshold <percent>   A head median more than this many percent slower than the base median is a
                                  regression when the p-value is below --pvalue (default: 5).
          --pvalue <p>            The two-sided Mann-Whitney U p-value below which a change counts, in (0, 1]
                                  (default: 0.01).
          --json-out <file>       Also write the comparison as JSON.
          -h, --help              Show this help.

        Exit code: 0 no regression, 1 at least one regression, 2 invalid arguments or reports.
        """;

    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>bench-compare</c>.</param>
    /// <param name="output">Receives the table and the help.</param>
    /// <param name="error">Receives the error messages.</param>
    /// <returns><see cref="ExitSuccess"/>, <see cref="ExitRegression"/> or <see cref="ExitInvalidInput"/>.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out var options, out var message))
        {
            if (message.Length == 0)
            {
                output.WriteLine(Usage);
                return ExitSuccess;
            }

            error.WriteLine(message);
            error.WriteLine();
            error.WriteLine(Usage);
            return ExitInvalidInput;
        }

        ComparisonReport report;
        try
        {
            var baseRuns = BenchmarkReportReader.Read(options.BasePath, "base");
            var headRuns = BenchmarkReportReader.Read(options.HeadPath, "head");
            report = BenchmarkComparer.Compare(baseRuns, headRuns, options.ThresholdPercent, options.PValue);
        }
        catch (BenchmarkReportException exception)
        {
            error.WriteLine(exception.Message);
            return ExitInvalidInput;
        }

        ComparisonReportWriter.WriteTable(output, report, options.ThresholdPercent, options.PValue);
        if (options.JsonOut is not null)
        {
            ComparisonReportWriter.WriteJson(options.JsonOut, report, options.ThresholdPercent, options.PValue);
        }

        if (report.Comparisons.Count == 0)
        {
            error.WriteLine("No benchmark appears in both base and head.");
            return ExitInvalidInput;
        }

        return report.Count(ComparisonStatus.Regression) > 0 ? ExitRegression : ExitSuccess;
    }

    // False with an empty message for --help
    private static bool TryParse(string[] args, out Options options, out string message)
    {
        options = new Options("", "", DefaultThresholdPercent, DefaultPValue, null);
        var positional = new List<string>(2);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    message = "";
                    return false;
                case "--threshold" or "--pvalue" or "--json-out":
                    if (i + 1 >= args.Length)
                    {
                        message = $"{arg} requires a value.";
                        return false;
                    }

                    var value = args[++i];
                    if (arg == "--json-out")
                    {
                        options = options with { JsonOut = value };
                    }
                    else if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                 out var number) ||
                             !double.IsFinite(number))
                    {
                        message = $"{arg} requires a number, got: {value}";
                        return false;
                    }
                    else if (arg == "--threshold")
                    {
                        options = options with { ThresholdPercent = number };
                    }
                    else if (number is > 0 and <= 1)
                    {
                        options = options with { PValue = number };
                    }
                    else
                    {
                        message = $"--pvalue requires a number in (0, 1], got: {value}";
                        return false;
                    }

                    break;
                default:
                    if (arg.StartsWith('-') && !File.Exists(arg) && !Directory.Exists(arg))
                    {
                        message = $"Unknown option: {arg}";
                        return false;
                    }

                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count != 2)
        {
            message = $"Expected <base> and <head>, got {positional.Count} path(s).";
            return false;
        }

        options = options with { BasePath = positional[0], HeadPath = positional[1] };
        message = "";
        return true;
    }

    private sealed record Options(
        string BasePath,
        string HeadPath,
        double ThresholdPercent,
        double PValue,
        string? JsonOut);
}
