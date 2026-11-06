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
          dotnet run --project tools/smell-scan -c Release -- [options] [paths...]

        Measures size and nesting smells of C# files with Roslyn and prints a JSON report.
        With no path and no --files-from, every tracked .cs file of the repository is scanned.

        Options:
          --config <file>      Smells file (default: tools/smell-scan/smells.yml of the root).
          --files-from <json>  JSON array of repository-relative paths to scan.
          --root <dir>         Repository root (default: the checkout that holds the current directory).
          --out <file>         Write the report here instead of standard output.
          --format json        Output format (only json for now).
          --parallelism <n>    Number of files measured at once (default: processor count).
          --help               Show this text.

        Exit codes: 0 scan done, 1 internal error, 2 wrong command line or configuration.
        """;

    /// <summary>Runs the tool.</summary>
    public static int Main(string[] args)
    {
        try
        {
            return Run(CommandLineParser.Parse(args));
        }
        catch (ArgumentException exception)
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

        var root = options.Root is null ? RepositoryRoot.Discover(Directory.GetCurrentDirectory()) : Path.GetFullPath(options.Root);
        var configFile = options.Config is null ? Path.Combine(root, DefaultConfig) : Path.GetFullPath(options.Config);
        if (!File.Exists(configFile))
        {
            throw new ArgumentException($"The configuration file '{configFile}' does not exist.");
        }

        var configText = File.ReadAllText(configFile);
        var configPath = SourceFileSet.Normalize(Path.GetRelativePath(root, configFile));
        var config = SmellConfigReader.Read(configPath, configText);

        var selection = Select(root, options);
        foreach (var missing in selection.Missing)
        {
            Console.Error.WriteLine($"warning: '{missing}' is not a file; skipped.");
        }

        var runner = new ScanRunner(root, config, options.Parallelism ?? Environment.ProcessorCount);
        var report = runner.Run(selection.Files, configPath, configText);
        var bytes = JsonReportWriter.Write(report);
        if (options.Out is null)
        {
            using var output = Console.OpenStandardOutput();
            output.Write(bytes);
        }
        else
        {
            File.WriteAllBytes(options.Out, bytes);
        }

        return 0;
    }

    private static FileSelection Select(string root, CommandLineOptions options)
    {
        if (options.FilesFrom is not null)
        {
            return SourceFileSet.FromJsonList(root, File.ReadAllText(options.FilesFrom));
        }

        return options.Paths.Count > 0 ? SourceFileSet.FromPaths(root, options.Paths) : SourceFileSet.AllTracked(root);
    }
}
