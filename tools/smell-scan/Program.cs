using System.Diagnostics;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Entry point of <c>tools/smell-scan</c>. Exit codes: 0 when the scan ran (findings do not change it, because they
/// are triggers for review), 2 for a wrong command line or configuration, 1 for an internal error.
/// </summary>
public static class Program
{
    internal const string DefaultConfig = "tools/smell-scan/smells.yml";

    internal const string HelpText =
        """
        Usage:
          node scripts/human/smell-scan.js [options] [paths...]
          dotnet run --project tools/smell-scan -c Release -- [options] [paths...]

        Measures size, nesting and error-handling smells of C# files with Roslyn and prints a report.
        With no path, --files-from or --changed, every tracked .cs file of the repository is scanned.

        Options:
          --config <file>      Smells file (default: tools/smell-scan/smells.yml of the root).
          --files-from <json>  JSON array of repository-relative paths to scan.
          --changed <base>     Scan the .cs files that differ between <base> and HEAD (git diff <base>...HEAD).
                               The files are read from the working tree, so uncommitted edits are measured.
                               A partial type is added up from the files scanned, so list all its parts for a full size.
          --root <dir>         Repository root (default: the checkout that holds the current directory).
          --baseline <json>    A JSON report of an earlier run (same configuration); report only the findings whose
                               fingerprint it does not have, that is, the new ones.
          --out <file>         Write the report here instead of standard output. With several formats it is a directory
                               that receives smells.json, smells.sarif, smells.md and smells.txt.
          --format <names>     json (default), sarif, text, markdown or github (GitHub Actions warnings, for a new-findings
                               run); comma-separated for several, as in json,sarif,markdown.
          --report <name>      findings (default) or metrics. metrics lists the largest measurements of each rule,
                               above the threshold or not, as text.
          --top <n>            Measurements of each rule that --report metrics lists (default 10).
          --timing             Write the time of each phase to standard error.
          --parallelism <n>    Number of files measured at once (default: processor count).
          --help               Show this text.

        Exit codes: 0 scan done, 1 internal error, 2 wrong command line or configuration.
        """;

    private const int DefaultTop = 10;

    /// <summary>Runs the tool.</summary>
    public static int Main(string[] args)
    {
        try
        {
            return Run(CommandLineParser.Parse(args));
        }
        catch (UsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("Use --help for the usage.");
            return 2;
        }
        catch (SmellConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            // Anything else is a bug or an environment problem; say so with the full details and fail with 1.
            Console.Error.WriteLine($"smell-scan internal error: {exception}");
            return 1;
        }
    }

    private static int Run(CommandLineOptions options)
    {
        if (options.Help)
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        var clock = Stopwatch.StartNew();
        var root = options.Root is null ? RepositoryRoot.Discover(Directory.GetCurrentDirectory()) : Path.GetFullPath(options.Root);
        var configFile = options.Config is null ? Path.Combine(root, DefaultConfig) : Path.GetFullPath(options.Config);
        if (!File.Exists(configFile))
        {
            throw new UsageException($"The configuration file '{configFile}' does not exist.");
        }

        var configText = File.ReadAllText(configFile);
        var configPath = SourceFileSet.Normalize(Path.GetRelativePath(root, configFile));
        var config = SmellConfigReader.Read(configPath, configText);

        var selection = Select(root, options);
        foreach (var missing in selection.Missing)
        {
            Console.Error.WriteLine($"warning: '{missing}' is not a file; skipped.");
        }

        var setup = clock.Elapsed;
        var metricsTop = options.Report == ReportMode.Metrics ? options.Top ?? DefaultTop : 0;
        var runner = new ScanRunner(root, config, options.Parallelism ?? Environment.ProcessorCount, metricsTop);
        var report = runner.Run(selection.Files, configPath, configText);
        if (options.Baseline is not null)
        {
            report = BaselineFilter.KeepNew(report, File.ReadAllText(options.Baseline));
        }

        clock.Restart();
        WriteReports(report, options);

        if (options.Timing)
        {
            WriteTiming(setup, runner, clock.Elapsed, report.Files);
        }

        return 0;
    }

    private static void WriteReports(ScanReport report, CommandLineOptions options)
    {
        if (options.Formats.Count > 1 && options.Out is { } directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var format in options.Formats)
            {
                File.WriteAllBytes(Path.Combine(directory, ReportFormatFiles.NameOf(format)), Write(report, options, format));
            }

            return;
        }

        var single = options.Formats.Count > 0 ? options.Formats[0] : ReportFormat.Json;
        var bytes = Write(report, options, single);
        if (options.Out is null)
        {
            using var output = Console.OpenStandardOutput();
            output.Write(bytes);
        }
        else
        {
            File.WriteAllBytes(options.Out, bytes);
        }
    }

    private static byte[] Write(ScanReport report, CommandLineOptions options, ReportFormat format)
    {
        if (options.Report == ReportMode.Metrics)
        {
            return TextReportWriter.WriteMetrics(report);
        }

        return format switch
        {
            ReportFormat.Sarif => SarifReportWriter.Write(report),
            ReportFormat.Text => TextReportWriter.WriteFindings(report),
            ReportFormat.Markdown => MarkdownReportWriter.Write(report),
            ReportFormat.Github => GithubAnnotationWriter.Write(report),
            _ => JsonReportWriter.Write(report),
        };
    }

    private static void WriteTiming(TimeSpan setup, ScanRunner runner, TimeSpan write, int files)
    {
        Console.Error.WriteLine($"timing: {files} files");
        Console.Error.WriteLine($"timing: setup (root, config, file list) {setup.TotalMilliseconds:F0} ms");
        Console.Error.WriteLine($"timing: parse and measure {runner.MeasureTime.TotalMilliseconds:F0} ms");
        Console.Error.WriteLine($"timing: merge and judge {runner.JudgeTime.TotalMilliseconds:F0} ms");
        Console.Error.WriteLine($"timing: write {write.TotalMilliseconds:F0} ms");
    }

    private static FileSelection Select(string root, CommandLineOptions options)
    {
        if (options.FilesFrom is not null)
        {
            return SourceFileSet.FromJsonList(root, File.ReadAllText(options.FilesFrom));
        }

        if (options.Changed is not null)
        {
            return SourceFileSet.FromChanged(root, options.Changed);
        }

        return options.Paths.Count > 0 ? SourceFileSet.FromPaths(root, options.Paths) : SourceFileSet.AllTracked(root);
    }
}
