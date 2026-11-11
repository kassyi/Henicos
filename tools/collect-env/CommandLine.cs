using System.Text;
using static Privacy;

/// <summary>
/// What the command line asks for; <see cref="ExitCode"/> is set when the run ends before the report.
/// </summary>
internal readonly record struct CommandLineOptions(bool Json, string? OutputPath, int? ExitCode);

/// <summary>Reads the arguments and writes the report to standard output or a file.</summary>
internal static class CommandLine
{
    public const string HelpText =
        """
        collect-env: prints the environment of a G# bug report (OS, CPU, GPU, CUDA, host C++ compiler, .NET, checkout).

        Usage: collect-env [--output <file>] [--json] [--help]

          --output <file>   write the report to <file> instead of stdout
          --json            machine-readable JSON instead of Markdown
          --help            show this text

        The report leaves out the user name, host name, serial numbers and paths under the user profile (shown as ~).
        CUDA_PATH is reported as set or unset only. A missing tool is shown as "not found".

        """;

    public static CommandLineOptions Parse(string[] args)
    {
        bool json = false;
        string? outputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.Write(HelpText);
                    return Stop(0);
                case "--json":
                    json = true;
                    break;
                case "--output" when i + 1 >= args.Length:
                    Console.Error.WriteLine("collect-env: --output needs a file path.");
                    return Stop(2);
                case "--output":
                    outputPath = args[++i];
                    break;
                case var other when other.StartsWith("--output=", StringComparison.Ordinal):
                    outputPath = other.Substring("--output=".Length);
                    break;
                default:
                    Console.Error.WriteLine($"collect-env: unknown option '{arg}'. Use --help.");
                    return Stop(2);
            }
        }

        return new CommandLineOptions(json, outputPath, ExitCode: null);
    }

    public static int WriteReport(string text, string? outputPath)
    {
        if (outputPath is null)
        {
            Console.Out.Write(text);
            return 0;
        }

        try
        {
            File.WriteAllText(outputPath, text, new UTF8Encoding(false));
            Console.Out.WriteLine($"collect-env: wrote {Clean(Path.GetFullPath(outputPath))}");
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"collect-env: cannot write '{outputPath}': {Clean(ex.Message)}");
            return 1;
        }
    }

    private static CommandLineOptions Stop(int exitCode) => new(Json: false, OutputPath: null, exitCode);
}
