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

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.CreateFrom(
        FuzzOptionTable.GenerationRows,
        new("--size", FuzzOptionTable.Value(FuzzOptionTable.ParseSize)),
        new("--profile", FuzzOptionTable.Value(FuzzOptionTable.ParseProfileCode)),
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
        var values = new FuzzOptionValues();
        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return error is null;
        }

        FuzzSwarmConfig.TryParseFocus(values.SwarmFocus, out var swarmFocus);
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

        string outDir = values.OutDir ?? Path.Combine("tmp", "differential-fuzz", "Generated");
        options = new GenCommandOptions(fuzzOptions, outDir, values.RepoRoot, values.CudaArch);
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
