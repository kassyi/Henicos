using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Parsed command-line options for <c>fuzz-diff gen</c>.
/// </summary>
public sealed record GenCommandOptions(
    FuzzOptions FuzzOptions,
    string OutDir,
    string? RepoRoot,
    string? CudaArch);

/// <summary>
/// Implements the <c>fuzz-diff gen</c> subcommand.
/// </summary>
public sealed class GenCommand : IFuzzCommand
{
    private static readonly string s_defaultOutDir = Path.Combine("tmp", "differential-fuzz", "Generated");

    /// <summary>Gets the singleton command instance.</summary>
    public static GenCommand Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "gen";

    /// <summary>Runs the <c>gen</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args) => ((IFuzzCommand)Instance).Run(args);

    /// <inheritdoc/>
    int IFuzzCommand.Run(ReadOnlySpan<string> args)
    {
        if (!TryParseOptions(args, out var options, out bool showHelp, out string? error))
        {
            return Program.FailUsage(error ?? "Invalid options for 'gen'.");
        }

        if (showHelp)
        {
            Console.Out.WriteLine(Program.HelpText);
            return 0;
        }

        return Execute(options!);
    }

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.CreateTable(
        new("--fuzz-seed", FuzzOptionTable.Value(FuzzOptionTable.ParseFuzzSeed)),
        new("--input-seed", FuzzOptionTable.Value(FuzzOptionTable.ParseInputSeed)),
        new("--batch-size", FuzzOptionTable.Value(FuzzOptionTable.ParseBatchSize)),
        new("--size", FuzzOptionTable.Value(FuzzOptionTable.ParseSize)),
        new("--profile", FuzzOptionTable.Value(FuzzOptionTable.ParseProfileCode)),
        new("--allow-issue", FuzzOptionTable.Value(FuzzOptionTable.ParseAllowIssue)),
        new("--swarm-focus", FuzzOptionTable.Value(FuzzOptionTable.ParseSwarmFocus)),
        new("--metamorphic", FuzzOptionTable.Flag(static v => v.Metamorphic = true)),
        new("--out-dir", FuzzOptionTable.Value(FuzzOptionTable.ParseOutDir)),
        new("--repo-root", FuzzOptionTable.Value(FuzzOptionTable.ParseRepoRoot)),
        new("--cuda-arch", FuzzOptionTable.Value(FuzzOptionTable.ParseCudaArch)));

    /// <summary>Parses command-line arguments into a <see cref="GenCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out GenCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        var values = new FuzzOptionValues { OutDir = s_defaultOutDir };

        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return showHelp;
        }

        FuzzSwarmFocus swarmFocus = FuzzSwarmFocus.Default;
        if (values.SwarmFocus is not null)
        {
            FuzzSwarmConfig.TryParseFocus(values.SwarmFocus, out swarmFocus);
        }

        var fuzzOptions = new FuzzOptions
        {
            FuzzSeed = values.FuzzSeed,
            InputSeed = values.InputSeed,
            BatchSize = values.BatchSize,
            Size = values.Size,
            Profile = values.ProfileCode,
            AllowedIssues = new HashSet<int>(values.AllowedIssues),
            SwarmFocus = swarmFocus,
            Metamorphic = values.Metamorphic,
        };

        options = new GenCommandOptions(
            fuzzOptions,
            values.OutDir ?? s_defaultOutDir,
            values.RepoRoot,
            values.CudaArch);
        return true;
    }

    /// <summary>Executes the <c>gen</c> subcommand using the parsed <paramref name="options"/>.</summary>
    public static int Execute(GenCommandOptions options)
    {
        var generator = new ScalarFuzzGenerator(options.FuzzOptions);
        var batch = generator.GenerateBatch();
        FuzzBatchWorkspaceWriter.WriteBatch(batch, options.OutDir, options.RepoRoot, options.CudaArch);

        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"fuzz-diff gen: wrote {batch.Kernels.Count} kernels (fuzz-seed={FuzzRandom.FormatSeed(options.FuzzOptions.FuzzSeed)}, profile={options.FuzzOptions.Profile}) to {Path.GetFullPath(options.OutDir)}"));
        return 0;
    }
}
