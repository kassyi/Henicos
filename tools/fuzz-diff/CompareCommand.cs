using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Parsed command-line options for <c>fuzz-diff compare</c>.
/// </summary>
/// <param name="BaseRef">The git ref whose compiler output the working tree is compared with.</param>
/// <param name="Batches">The number of generated batches.</param>
/// <param name="Generation">The generator options of the first batch; batch <c>n</c> uses fuzz seed <c>FuzzSeed + n</c>.</param>
/// <param name="Dialects">The dialects to build, <c>Cpu</c> and/or <c>Cuda</c>.</param>
/// <param name="ExpectChange">Whether a difference is the expected result (exit code 0).</param>
/// <param name="NoReduce">Whether to skip reducing the kernels that differ.</param>
/// <param name="ReduceMinutes">The time limit of one reduction.</param>
/// <param name="WorkDir">The directory for the base worktree, the compare projects and the findings.</param>
/// <param name="RepoRoot">The root of the working tree.</param>
public sealed record CompareCommandOptions(
    string BaseRef,
    int Batches,
    FuzzOptions Generation,
    IReadOnlyList<string> Dialects,
    bool ExpectChange,
    bool NoReduce,
    int ReduceMinutes,
    string WorkDir,
    string RepoRoot);

/// <summary>
/// Implements the <c>fuzz-diff compare</c> subcommand: generates kernel batches and compares what the G# generator
/// of a base ref and of the working tree write for them (native module and generated C#), without a GPU.
/// </summary>
public sealed class CompareCommand : IFuzzCommand
{
    /// <summary>Exit code when the outputs differ and <c>--expect-change</c> is not given.</summary>
    public const int ExitDifference = 1;

    /// <summary>Exit code when a build or git command fails.</summary>
    public const int ExitInfrastructureFailure = 3;

    /// <summary>The batch count when <c>--batches</c> is not given.</summary>
    public const int DefaultBatches = 20;

    /// <summary>The time limit of one reduction, in minutes, when <c>--reduce-minutes</c> is not given.</summary>
    public const int DefaultReduceMinutes = 30;

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.Create(
        new("--base", FuzzOptionTable.Value(ParseBase)),
        new("--profile", FuzzOptionTable.Value(ParseProfile)),
        new("--dialects", FuzzOptionTable.Value(ParseDialects)),
        new("--reduce-minutes", FuzzOptionTable.Value(ParseReduceMinutes)),
        new("--work-dir", FuzzOptionTable.Value(FuzzOptionTable.ParseWorkDir)),
        new("--repo-root", FuzzOptionTable.Value(FuzzOptionTable.ParseRepoRoot)));

    /// <summary>Gets the singleton command instance.</summary>
    public static CompareCommand Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "compare";

    /// <inheritdoc/>
    int IFuzzCommand.Run(ReadOnlySpan<string> args)
    {
        if (!TryParseOptions(args, out var options, out bool showHelp, out string? error))
        {
            return Program.FailUsage(error ?? "Invalid options for 'compare'.");
        }

        if (showHelp || options is null)
        {
            Console.Out.WriteLine(Program.HelpText);
            return 0;
        }

        return Execute(options);
    }

    /// <summary>Parses command-line arguments into a <see cref="CompareCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out CompareCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        var values = new FuzzOptionValues();
        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return error is null;
        }

        if (string.IsNullOrWhiteSpace(values.CompareBase))
        {
            error = "--base <ref> is required.";
            return false;
        }

        FuzzSwarmConfig.TryParseFocus(values.SwarmFocus, out var swarmFocus);
        var generation = new FuzzOptions
        {
            FuzzSeed = values.FuzzSeed,
            InputSeed = values.InputSeed,
            BatchSize = values.BatchSize,
            Profile = values.Profile.ToCode(),
            AllowedIssues = new HashSet<int>(values.AllowedIssues),
            SwarmFocus = swarmFocus,
            Metamorphic = values.Metamorphic,
        };

        options = new CompareCommandOptions(
            BaseRef: values.CompareBase,
            Batches: values.Batches ?? DefaultBatches,
            Generation: generation,
            Dialects: values.Dialects,
            ExpectChange: values.ExpectChange,
            NoReduce: values.NoReduce,
            ReduceMinutes: values.ReduceMinutes,
            WorkDir: Path.GetFullPath(values.WorkDir ?? Path.Combine("tmp", "differential-fuzz")),
            RepoRoot: Path.GetFullPath(values.RepoRoot ?? Directory.GetCurrentDirectory()));
        return true;
    }

    /// <summary>Runs the comparison and returns the exit code; a build or git failure exits with 3.</summary>
    public static int Execute(CompareCommandOptions options)
    {
        try
        {
            return CodegenCompareRunner.Run(options);
        }
        catch (CompareInfrastructureException ex)
        {
            Console.Error.WriteLine($"fuzz-diff compare: {ex.Message}");
            return ExitInfrastructureFailure;
        }
    }

    /// <summary>Returns the dialects compared when <c>--dialects</c> is not given: <c>Cpu</c>, <c>Cuda</c>.</summary>
    internal static List<string> DefaultDialects() => ["Cpu", "Cuda"];

    private static string? ParseBase(FuzzOptionValues values, string value)
    {
        values.CompareBase = value;
        return null;
    }

    // Unlike `run`, an unknown profile is read as C (KnownIssueFilter.NormalizeProfile).
    private static string? ParseProfile(FuzzOptionValues values, string value)
    {
        values.Profile = FuzzProfileCodes.FromCode(KnownIssueFilter.NormalizeProfile(value));
        return null;
    }

    private static string? ParseDialects(FuzzOptionValues values, string value) =>
        TryParseDialects(value, values.Dialects)
            ? null
            : $"Invalid --dialects '{value}'. Expected Cpu, Cuda or Cpu,Cuda.";

    private static string? ParseReduceMinutes(FuzzOptionValues values, string value)
    {
        if (!TryParsePositive(value, out int minutes))
        {
            return "--reduce-minutes requires a positive integer.";
        }

        values.ReduceMinutes = minutes;
        return null;
    }

    private static bool TryParsePositive(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

    private static bool TryParseDialects(string text, List<string> dialects)
    {
        dialects.Clear();
        foreach (string part in FuzzSyntaxHelpers.SplitNonEmpty(text, ", "))
        {
            string? dialect =
                string.Equals(part, "Cpu", StringComparison.OrdinalIgnoreCase) ? "Cpu" :
                string.Equals(part, "Cuda", StringComparison.OrdinalIgnoreCase) ? "Cuda" :
                null;
            if (dialect is null)
            {
                return false;
            }

            if (!dialects.Contains(dialect))
            {
                dialects.Add(dialect);
            }
        }

        return dialects.Count > 0;
    }
}
