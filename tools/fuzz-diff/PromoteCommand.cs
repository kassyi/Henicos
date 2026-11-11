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

    private static readonly string s_defaultFindingsDir = Path.Combine("tmp", "differential-fuzz", "findings");

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

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.CreateTable(
        new("--source", FuzzOptionTable.Value(FuzzOptionSetters.ParseSourcePath)),
        new("--fuzz-seed", FuzzOptionTable.Value(FuzzOptionTable.ParseFuzzSeed)),
        new("--input-seed", FuzzOptionTable.Value(FuzzOptionTable.ParseInputSeed)),
        new("--failing-paths", FuzzOptionTable.Value(FuzzOptionSetters.ParseFailingPaths)),
        new("--failure-kind", FuzzOptionTable.Value(FuzzOptionSetters.ParseFailureKind)),
        new("--column", FuzzOptionTable.Value(FuzzOptionSetters.ParseColumn)),
        new("--findings-dir", FuzzOptionTable.Value(FuzzOptionSetters.ParseFindingsDir)),
        new("--kernel", FuzzOptionTable.Value(FuzzOptionSetters.ParseKernelName)));

    /// <summary>Parses command-line arguments into a <see cref="PromoteCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out PromoteCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        var values = new FuzzOptionValues { FindingsDir = s_defaultFindingsDir };

        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return showHelp;
        }

        if (string.IsNullOrWhiteSpace(values.SourcePath) || !File.Exists(values.SourcePath))
        {
            error = "--source requires an existing .cu.cs file.";
            return false;
        }

        options = new PromoteCommandOptions(
            values.SourcePath,
            values.FuzzSeed,
            values.InputSeed ?? 0,
            values.FailingPaths,
            values.FailureKind,
            values.Column,
            values.FindingsDir ?? s_defaultFindingsDir,
            values.KernelName);
        return true;
    }

    /// <summary>
    /// Returns the arguments that <see cref="TryParseOptions"/> reads back into <paramref name="options"/>;
    /// <c>--kernel</c> is left out when <see cref="PromoteCommandOptions.KernelName"/> is <see langword="null"/>.
    /// </summary>
    internal static List<string> ToArguments(PromoteCommandOptions options)
    {
        var args = new List<string>
        {
            "--source", options.SourcePath,
            "--fuzz-seed", FuzzRandom.FormatSeed(options.FuzzSeed),
            "--input-seed", options.InputSeed.ToString(CultureInfo.InvariantCulture),
            "--failing-paths", options.FailingPaths,
            "--failure-kind", options.FailureKind,
            "--column", options.Column,
            "--findings-dir", options.FindingsDir,
        };
        if (options.KernelName is not null)
        {
            args.Add("--kernel");
            args.Add(options.KernelName);
        }

        return args;
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
