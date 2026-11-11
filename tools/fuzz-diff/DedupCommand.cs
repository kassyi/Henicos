using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Parsed command-line options for <c>fuzz-diff dedup</c>.
/// </summary>
public sealed record DedupCommandOptions(
    string SourcePath,
    string FailingPaths,
    string FailureKind,
    bool Json);

/// <summary>
/// Implements the <c>fuzz-diff dedup</c> subcommand.
/// </summary>
public sealed class DedupCommand : IFuzzCommand
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Gets the singleton command instance.</summary>
    public static DedupCommand Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "dedup";

    /// <summary>Runs the <c>dedup</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args) => ((IFuzzCommand)Instance).Run(args);

    /// <inheritdoc/>
    int IFuzzCommand.Run(ReadOnlySpan<string> args)
    {
        if (!TryParseOptions(args, out var options, out bool showHelp, out string? error))
        {
            return Program.FailUsage(error ?? "Invalid options for 'dedup'.");
        }

        if (showHelp)
        {
            Console.Out.WriteLine(Program.HelpText);
            return 0;
        }

        return Execute(options!);
    }

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.CreateTable(
        new("--source", FuzzOptionTable.Value(FuzzOptionSetters.ParseSourcePath)),
        new("--failing-paths", FuzzOptionTable.Value(FuzzOptionSetters.ParseFailingPaths)),
        new("--failure-kind", FuzzOptionTable.Value(FuzzOptionSetters.ParseFailureKind)),
        new("--json", FuzzOptionTable.Flag(static v => v.Json = true)));

    /// <summary>Parses command-line arguments into a <see cref="DedupCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out DedupCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        var values = new FuzzOptionValues
        {
            FailingPaths = "Native,Cuda",
            FailureKind = "CellMismatch",
        };

        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return showHelp;
        }

        if (string.IsNullOrWhiteSpace(values.SourcePath) || !File.Exists(values.SourcePath))
        {
            error = "--source requires an existing .cu.cs file.";
            return false;
        }

        options = new DedupCommandOptions(values.SourcePath, values.FailingPaths, values.FailureKind, values.Json);
        return true;
    }

    /// <summary>Executes the <c>dedup</c> subcommand using the parsed <paramref name="options"/>.</summary>
    public static int Execute(DedupCommandOptions options)
    {
        string source = File.ReadAllText(options.SourcePath, s_utf8NoBom);
        var fp = FuzzDeduplicator.ComputeFingerprint(source, options.FailingPaths, options.FailureKind);
        if (options.Json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                fingerprint = fp.Fingerprint,
                hash64 = string.Create(CultureInfo.InvariantCulture, $"0x{fp.Hash64:X16}"),
                failingPaths = fp.FailingPathsMask,
                failureKind = fp.FailureKindTag,
                normalizedAstSkeleton = fp.NormalizedAstSkeleton,
            }));
        }
        else
        {
            Console.Out.WriteLine(fp.Fingerprint);
        }

        return 0;
    }
}
