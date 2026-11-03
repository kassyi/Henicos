using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Parsed command-line options for <c>fuzz-diff promote</c>.
/// </summary>
public sealed record PromoteCommandOptions(
    string SourcePath,
    ulong FuzzSeed,
    int InputSeed,
    string FailingPaths,
    string FailureKind,
    string Column,
    string FindingsDir,
    string? KernelName);

/// <summary>
/// Implements the <c>fuzz-diff promote</c> subcommand.
/// </summary>
public sealed class PromoteCommand : IFuzzCommand
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Gets the singleton command instance.</summary>
    public static PromoteCommand Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "promote";

    /// <summary>Runs the <c>promote</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args) => ((IFuzzCommand)Instance).Run(args);

    /// <inheritdoc/>
    int IFuzzCommand.Run(ReadOnlySpan<string> args)
    {
        if (!TryParseOptions(args, out var options, out bool showHelp, out string? error))
        {
            return Program.FailUsage(error ?? "Invalid options for 'promote'.");
        }

        if (showHelp)
        {
            Console.Out.WriteLine(Program.HelpText);
            return 0;
        }

        return Execute(options!);
    }

    /// <summary>Parses command-line arguments into a <see cref="PromoteCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out PromoteCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        showHelp = false;
        error = null;

        string? sourcePath = null;
        ulong fuzzSeed = 1UL;
        int inputSeed = 0;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        string column = "hash";
        string findingsDir = Path.Combine("tmp", "differential-fuzz", "findings");
        string? kernelName = null;

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

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        error = "--fuzz-seed requires a 64-bit integer.";
                        return false;
                    }

                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
                    {
                        error = "--input-seed requires an integer.";
                        return false;
                    }

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

                case "--column":
                    if (i + 1 >= args.Length)
                    {
                        error = "--column requires a column name.";
                        return false;
                    }

                    column = args[++i];
                    break;

                case "--findings-dir":
                    if (i + 1 >= args.Length)
                    {
                        error = "--findings-dir requires a directory path.";
                        return false;
                    }

                    findingsDir = args[++i];
                    break;

                case "--kernel":
                    if (i + 1 >= args.Length)
                    {
                        error = "--kernel requires a kernel method name.";
                        return false;
                    }

                    kernelName = args[++i];
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

        options = new PromoteCommandOptions(
            sourcePath,
            fuzzSeed,
            inputSeed,
            failingPaths,
            failureKind,
            column,
            findingsDir,
            kernelName);
        return true;
    }

    /// <summary>Executes the <c>promote</c> subcommand using the parsed <paramref name="options"/>.</summary>
    public static int Execute(PromoteCommandOptions options)
    {
        string source = File.ReadAllText(options.SourcePath, s_utf8NoBom);
        var artifacts = FuzzPromoter.BuildPromotionArtifacts(
            source,
            options.FuzzSeed,
            options.InputSeed,
            options.FailingPaths,
            options.FailureKind,
            targetKernelName: options.KernelName,
            column: options.Column);
        string writtenPath = FuzzPromoter.WritePromotedFinding(options.FindingsDir, artifacts, options.FuzzSeed);

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            status = "promoted",
            fingerprint = artifacts.Fingerprint,
            reproKernelPath = writtenPath,
            reproClassName = artifacts.ReproKernelClassName,
            reproMethodName = artifacts.ReproKernelMethodName,
            knownDivergenceEntry = artifacts.KnownDivergenceEntry,
        }));
        return 0;
    }
}
