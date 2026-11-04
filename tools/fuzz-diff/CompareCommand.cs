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

    /// <summary>Parses command-line arguments into a <see cref="CompareCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out CompareCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        showHelp = false;
        error = null;

        string? baseRef = null;
        int batches = 20;
        ulong fuzzSeed = 1UL;
        int? inputSeed = null;
        int batchSize = 64;
        string profile = "C";
        var allowedIssues = new HashSet<int>();
        var swarmFocus = FuzzSwarmFocus.Default;
        bool metamorphic = false;
        var dialects = new List<string> { "Cpu", "Cuda" };
        bool expectChange = false;
        bool noReduce = false;
        int reduceMinutes = 30;
        string workDir = Path.Combine("tmp", "differential-fuzz");
        string repoRoot = Directory.GetCurrentDirectory();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            bool hasValue = i + 1 < args.Length;
            switch (arg)
            {
                case "--help" or "-h":
                    showHelp = true;
                    return true;

                case "--base" when hasValue:
                    baseRef = args[++i];
                    break;

                case "--batches" when hasValue && TryParsePositive(args[++i], out batches):
                    break;

                case "--batch-size" when hasValue && TryParsePositive(args[++i], out batchSize) && batchSize <= 256:
                    break;

                case "--fuzz-seed" when hasValue && FuzzRandom.TryParseSeed(args[++i], out fuzzSeed):
                    break;

                case "--input-seed" when hasValue && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed):
                    inputSeed = seed;
                    break;

                case "--profile" when hasValue:
                    profile = KnownIssueFilter.NormalizeProfile(args[++i]);
                    break;

                case "--allow-issue" when hasValue &&
                    int.TryParse(args[++i].AsSpan().TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int issue) &&
                    issue > 0:
                    allowedIssues.Add(issue);
                    break;

                case "--swarm-focus" when hasValue && FuzzSwarmConfig.TryParseFocus(args[++i], out swarmFocus):
                    break;

                case "--metamorphic":
                    metamorphic = true;
                    break;

                case "--dialects" when hasValue && TryParseDialects(args[++i], dialects):
                    break;

                case "--expect-change":
                    expectChange = true;
                    break;

                case "--no-reduce":
                    noReduce = true;
                    break;

                case "--reduce-minutes" when hasValue && TryParsePositive(args[++i], out reduceMinutes):
                    break;

                case "--work-dir" when hasValue:
                    workDir = args[++i];
                    break;

                case "--repo-root" when hasValue:
                    repoRoot = args[++i];
                    break;

                default:
                    error = $"Invalid or incomplete option '{arg}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(baseRef))
        {
            error = "--base <ref> is required.";
            return false;
        }

        var generation = new FuzzOptions
        {
            FuzzSeed = fuzzSeed,
            InputSeed = inputSeed,
            BatchSize = batchSize,
            Profile = profile,
            AllowedIssues = allowedIssues,
            SwarmFocus = swarmFocus,
            Metamorphic = metamorphic,
        };

        options = new CompareCommandOptions(
            baseRef,
            batches,
            generation,
            dialects,
            expectChange,
            noReduce,
            reduceMinutes,
            Path.GetFullPath(workDir),
            Path.GetFullPath(repoRoot));
        return true;
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
