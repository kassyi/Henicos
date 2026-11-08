namespace Kassyi.GSharp.Compiler.Generators;

public static class Program
{
    /// <summary>The exit code for a wrong command line: an unknown mode or a wrong number of arguments.</summary>
    public const int UsageExitCode = 2;

    /// <summary>The exit code for an input that a generator rejected (<see cref="GeneratorInputException"/>).</summary>
    public const int InputErrorExitCode = 1;

    private const string Usage =
        """
        Usage: Kassyi.GSharp.Compiler.Generators <mode> <arguments>
          diagnostics <diagnostics.json> <output directory>
          dialects <dialect YAML directory> <output directory>
          native-api <native API YAML directory> <output directory>
          prelude <resources directory> <output directory>
          internal-names <InternalNames.yml> <Prelude directory> <Core source directory> <output directory>
          demangle [<log|->] [<symbols>]   (demangle --help for details)
        """;

    public static int Main(string[] args) => Run(args, Console.Error);

    /// <summary>
    /// Runs the mode named by the first argument and returns the exit code: <c>0</c> on success,
    /// <see cref="InputErrorExitCode"/> when an input is rejected, <see cref="UsageExitCode"/> for a wrong command line.
    /// Errors go to <paramref name="error"/>.
    /// </summary>
    public static int Run(string[] args, TextWriter error)
    {
        if (args is ["demangle", ..])
        {
            return LogDemangler.Run(args[1..]);
        }

        Action? generator = args switch
        {
            ["diagnostics", var input, var output] => () => DiagnosticGenerator.Run(input, output),
            ["dialects", var input, var output] => () => YamlDialectGenerator.Run(input, output),
            ["native-api", var input, var output] => () => YamlApiGenerator.Run(input, output),
            ["prelude", var input, var output] => () => PreludeGenerator.Run(input, output),
            ["internal-names", var yaml, var prelude, var core, var output] =>
                () => InternalNamesGenerator.Run(yaml, prelude, core, output),
            _ => null,
        };

        if (generator is null)
        {
            error.WriteLine(DescribeUsageError(args));
            error.WriteLine(Usage);
            return UsageExitCode;
        }

        try
        {
            generator();
            return 0;
        }
        catch (GeneratorInputException ex)
        {
            error.WriteLine(ex.Message);
            return InputErrorExitCode;
        }
    }

    private static string DescribeUsageError(string[] args) => args switch
    {
        [] => "error : No mode given.",
        ["diagnostics" or "dialects" or "native-api" or "prelude" or "internal-names", ..] =>
            $"error : Wrong number of arguments for the mode '{args[0]}' ({args.Length - 1} given).",
        _ => $"error : Unknown mode '{args[0]}'.",
    };
}
