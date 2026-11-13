using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>The generation options of <c>fuzz-diff run</c>.</summary>
/// <param name="FuzzSeed">The seed of the first batch; batch <c>n</c> uses <c>FuzzSeed + n</c>.</param>
/// <param name="BatchSize">The kernels generated per batch.</param>
/// <param name="InputSeed">The explicit runtime data input seed, or <see langword="null"/> for the fixed seeds.</param>
/// <param name="AllowedIssues">The known issues whose avoided shapes are generated again.</param>
/// <param name="SwarmFocus">The optional swarm focus mode.</param>
/// <param name="Metamorphic">Whether metamorphic variants are generated.</param>
public sealed record RunGenerationOptions(
    ulong FuzzSeed,
    int BatchSize,
    int? InputSeed,
    IReadOnlyList<int> AllowedIssues,
    string? SwarmFocus,
    bool Metamorphic)
{
    /// <summary>Whether <c>--allow-issue</c> re-enabled the shapes of <paramref name="issue"/>.</summary>
    public bool IsAllowed(int issue)
    {
        foreach (int allowed in AllowedIssues)
        {
            if (allowed == issue)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>When <c>fuzz-diff run</c> stops.</summary>
/// <param name="Minutes">The run time limit in minutes; ignored when <paramref name="Batches"/> is set.</param>
/// <param name="Batches">The fixed number of batches, or <see langword="null"/> for a time limit.</param>
public sealed record RunLimits(double Minutes, int? Batches);

/// <summary>The directories of <c>fuzz-diff run</c>, all absolute.</summary>
/// <param name="RepoRoot">The root of the working tree.</param>
/// <param name="WorkDir">The workspace directory for the generated projects and the findings.</param>
/// <param name="OutDir">The directory for the generated sources.</param>
public sealed record RunPaths(string RepoRoot, string WorkDir, string OutDir);

/// <summary>
/// Parsed command-line options for <c>fuzz-diff run</c>.
/// </summary>
/// <param name="Profile">The paths the batches run on.</param>
/// <param name="Generation">The generation options.</param>
/// <param name="Limits">When the run stops.</param>
/// <param name="Paths">The directories.</param>
/// <param name="CompareBase">The git ref for <c>--compare-base</c>, or <see langword="null"/>.</param>
/// <param name="ExpectChange">Whether a difference found by <c>--compare-base</c> is the expected result.</param>
/// <param name="NoReduce">Whether failures are recorded without reduction.</param>
/// <param name="NoGpuLock">Whether the CUDA run skips <c>scripts/human/gpu-lock.js</c>.</param>
public sealed record RunCommandOptions(
    FuzzProfile Profile,
    RunGenerationOptions Generation,
    RunLimits Limits,
    RunPaths Paths,
    string? CompareBase,
    bool ExpectChange,
    bool NoReduce,
    bool NoGpuLock);

/// <summary>
/// Implements the <c>fuzz-diff run</c> subcommand: the differential fuzzer. It generates batches of kernels, builds and
/// runs them on the managed, native and CUDA paths, classifies the failures, reduces them and records findings.
/// <c>scripts/human/fuzz-differential.js</c> only builds this tool and passes its arguments here.
/// </summary>
public sealed class RunCommand : IFuzzCommand
{
    internal const string RunHelpText =
        """
        Usage: node scripts/human/fuzz-differential.js [options]
               dotnet run --project tools/fuzz-diff -- run [options]

          --profile <managed|cpu|all|M|C|G>  Execution profile: M/managed, C/cpu (default), or G/all (includes CUDA).
          --minutes <n>                      Run duration in minutes (default: 5 when --batches is not given).
          --batches <n>                      Run a fixed number of batches instead of a time limit.
          --batch-size <n>                   Kernels generated and compiled per batch (default: 64).
          --fuzz-seed <seed>                 Initial 64-bit fuzz seed in decimal or 0x hex (default: 1).
          --input-seed <int>                 Optional explicit runtime data input seed (default: 0..8).
          --allow-issue <n>                  Re-enable generation of shapes avoided for known issue #<n>
                                             (repeatable).
          --swarm-focus <mode>               Optional Swarm focus mode (default, coalesce-assign-value,
                                             tuple-deconstruct-complex, warp-shuffle, barrier, iterator,
                                             suspending-call, split-points).
          --metamorphic                      Generate semantics-preserving transformed kernel variants per kernel.
          --compare-base <ref>               Compare the generated native and C# code with git <ref>
                                             (fuzz-diff compare, no GPU).
          --expect-change                    Exit 0 when --compare-base finds differences.
          --work-dir <dir>                   Workspace directory for temporary projects
                                             (default: tmp/differential-fuzz).
          --out-dir <dir>                    Output directory for generated sources (default: <work-dir>/Generated).
          --no-reduce                        Skip automatic 2-stage reduction and fingerprint deduplication on
                                             failure.
          --no-gpu-lock                      Skip acquiring scripts/human/gpu-lock.js (use when already holding
                                             the lock).
          --repo-root <dir>                  Root of the working tree; relative directories resolve against it
                                             (default: current directory).
          --help, -h                         Show this help.
        """;

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.Create(
        new("--profile", FuzzOptionTable.Value(ParseProfile)),
        new("--minutes", FuzzOptionTable.Value(ParseMinutes)),
        new("--compare-base", FuzzOptionTable.Value(ParseCompareBase)),
        new("--work-dir", FuzzOptionTable.Value(FuzzOptionSetters.ParseWorkDir)),
        new("--out-dir", FuzzOptionTable.Value(FuzzOptionSetters.ParseOutDir)),
        new("--repo-root", FuzzOptionTable.Value(FuzzOptionSetters.ParseRepoRoot)),
        new("--no-gpu-lock", FuzzOptionTable.Flag(static v => v.NoGpuLock = true)));

    /// <summary>Gets the singleton command instance.</summary>
    public static RunCommand Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "run";

    /// <inheritdoc/>
    int IFuzzCommand.Run(ReadOnlySpan<string> args)
    {
        if (!TryParseOptions(args, out var options, out bool showHelp, out string? error))
        {
            Console.Error.WriteLine($"fuzz-diff run: {error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(RunHelpText);
            return 2;
        }

        if (showHelp || options is null)
        {
            Console.Out.WriteLine(RunHelpText);
            return 0;
        }

        try
        {
            return options.CompareBase is string compareBase
                ? CompareCommand.Execute(ToCompareOptions(options, compareBase))
                : new FuzzRunner(options).Run();
        }
        catch (FuzzRunAbortException ex)
        {
            Console.Error.WriteLine($"fuzz-diff run: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Parses command-line arguments into a <see cref="RunCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out RunCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        var values = new FuzzOptionValues();
        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return error is null;
        }

        string repoRoot = Path.GetFullPath(values.RepoRoot ?? Directory.GetCurrentDirectory());
        string workDir = values.WorkDir is null
            ? Path.Combine(repoRoot, "tmp", "differential-fuzz")
            : Path.GetFullPath(values.WorkDir, repoRoot);
        string outDir = values.OutDir is null
            ? Path.Combine(workDir, "Generated")
            : Path.GetFullPath(values.OutDir, repoRoot);

        options = new RunCommandOptions(
            Profile: values.Profile,
            Generation: new RunGenerationOptions(
                FuzzSeed: values.FuzzSeed,
                BatchSize: values.BatchSize,
                InputSeed: values.InputSeed,
                AllowedIssues: values.AllowedIssues,
                SwarmFocus: values.SwarmFocus,
                Metamorphic: values.Metamorphic),
            Limits: new RunLimits(Minutes: values.Minutes, Batches: values.Batches),
            Paths: new RunPaths(RepoRoot: repoRoot, WorkDir: workDir, OutDir: outDir),
            CompareBase: values.CompareBase,
            ExpectChange: values.ExpectChange,
            NoReduce: values.NoReduce,
            NoGpuLock: values.NoGpuLock);
        return true;
    }

    /// <summary>
    /// Builds the options of <c>fuzz-diff compare</c> for <c>--compare-base</c>: the batch count
    /// (<see cref="CompareCommand.DefaultBatches"/> when <c>--batches</c> is not given), the generation options, both
    /// dialects and the default reduction limit.
    /// </summary>
    internal static CompareCommandOptions ToCompareOptions(RunCommandOptions options, string compareBase)
    {
        var generation = options.Generation;
        FuzzSwarmConfig.TryParseFocus(generation.SwarmFocus, out var swarmFocus);
        return new CompareCommandOptions(
            BaseRef: compareBase,
            Batches: options.Limits.Batches ?? CompareCommand.DefaultBatches,
            Generation: new FuzzOptions
            {
                FuzzSeed = generation.FuzzSeed,
                InputSeed = generation.InputSeed,
                BatchSize = generation.BatchSize,
                Profile = options.Profile.ToCode(),
                AllowedIssues = new HashSet<int>(generation.AllowedIssues),
                SwarmFocus = swarmFocus,
                Metamorphic = generation.Metamorphic,
            },
            Dialects: CompareCommand.DefaultDialects(),
            ExpectChange: options.ExpectChange,
            NoReduce: options.NoReduce,
            ReduceMinutes: CompareCommand.DefaultReduceMinutes,
            WorkDir: options.Paths.WorkDir,
            RepoRoot: options.Paths.RepoRoot);
    }

    private static string? ParseProfile(FuzzOptionValues values, string value)
    {
        if (!FuzzProfileCodes.TryParseStrict(value, out var profile))
        {
            return $"Invalid --profile '{value}'. Expected managed|cpu|all|M|C|G.";
        }

        values.Profile = profile;
        return null;
    }

    private static string? ParseMinutes(FuzzOptionValues values, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) ||
            !double.IsFinite(minutes) ||
            minutes <= 0)
        {
            return "--minutes requires a positive number.";
        }

        values.Minutes = minutes;
        return null;
    }

    private static string? ParseCompareBase(FuzzOptionValues values, string value)
    {
        if (value.Length == 0)
        {
            return "--compare-base requires a git ref.";
        }

        values.CompareBase = value;
        return null;
    }
}
