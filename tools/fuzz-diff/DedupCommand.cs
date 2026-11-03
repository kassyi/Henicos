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

    /// <summary>Parses command-line arguments into a <see cref="DedupCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out DedupCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        showHelp = false;
        error = null;

        string? sourcePath = null;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        bool json = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    showHelp = true;
                    return true;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        error = "--source requires a file path.";
                        return false;
                    }

                    sourcePath = args[++i];
                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        error = "--failing-paths requires a value.";
                        return false;
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        error = "--failure-kind requires a value.";
                        return false;
                    }

                    failureKind = args[++i];
                    break;

                case "--json":
                    json = true;
                    break;

                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            error = "--source requires an existing .cu.cs file.";
            return false;
        }

        options = new DedupCommandOptions(sourcePath, failingPaths, failureKind, json);
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
