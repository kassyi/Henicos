using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Parsed command-line options for <c>fuzz-diff run</c>.
/// </summary>
/// <param name="Profile">The execution profile: <c>M</c>, <c>C</c> or <c>G</c>.</param>
/// <param name="Minutes">The run time limit in minutes; ignored when <paramref name="Batches"/> is set.</param>
/// <param name="Batches">The fixed number of batches, or <see langword="null"/> for a time limit.</param>
/// <param name="BatchSize">The kernels generated per batch.</param>
/// <param name="FuzzSeed">The seed of the first batch; batch <c>n</c> uses <c>FuzzSeed + n</c>.</param>
/// <param name="InputSeed">The explicit runtime data input seed, or <see langword="null"/> for the fixed seeds.</param>
/// <param name="AllowedIssues">The known issues whose avoided shapes are generated again.</param>
/// <param name="SwarmFocus">The optional swarm focus mode.</param>
/// <param name="Metamorphic">Whether metamorphic variants are generated.</param>
/// <param name="CompareBase">The git ref for <c>--compare-base</c>, or <see langword="null"/>.</param>
/// <param name="ExpectChange">Whether a difference found by <c>--compare-base</c> is the expected result.</param>
/// <param name="WorkDir">The workspace directory for the generated projects and the findings.</param>
/// <param name="OutDir">The directory for the generated sources.</param>
/// <param name="NoReduce">Whether failures are recorded without reduction.</param>
/// <param name="NoGpuLock">Whether the CUDA run skips <c>scripts/agent/gpu-lock.js</c>.</param>
/// <param name="RepoRoot">The root of the working tree.</param>
public sealed record RunCommandOptions(
    string Profile,
    double Minutes,
    int? Batches,
    int BatchSize,
    ulong FuzzSeed,
    int? InputSeed,
    List<int> AllowedIssues,
    string? SwarmFocus,
    bool Metamorphic,
    string? CompareBase,
    bool ExpectChange,
    string WorkDir,
    string OutDir,
    bool NoReduce,
    bool NoGpuLock,
    string RepoRoot);

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
          --allow-issue <n>                  Re-enable generation of shapes avoided for known issue #<n> (repeatable).
          --swarm-focus <mode>               Optional Swarm focus mode (default, coalesce-assign-value, tuple-deconstruct-complex).
          --metamorphic                      Generate semantics-preserving transformed kernel variants per kernel.
          --compare-base <ref>               Compare the generated native and C# code with git <ref> (fuzz-diff compare, no GPU).
          --expect-change                    Exit 0 when --compare-base finds differences.
          --work-dir <dir>                   Workspace directory for temporary projects (default: tmp/differential-fuzz).
          --out-dir <dir>                    Output directory for generated sources (default: <work-dir>/Generated).
          --no-reduce                        Skip automatic 2-stage reduction and fingerprint deduplication on failure.
          --no-gpu-lock                      Skip acquiring scripts/agent/gpu-lock.js (use when already holding the lock).
          --repo-root <dir>                  Root of the working tree; relative directories resolve against it (default: current directory).
          --help, -h                         Show this help.
        """;

    private const int DefaultCompareBatches = 20;

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
            return options.CompareBase is not null ? RunCompare(options) : new FuzzRunner(options).Run();
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
        showHelp = false;
        error = null;

        string profile = "C";
        double minutes = 5;
        int? batches = null;
        int batchSize = 64;
        ulong fuzzSeed = 1UL;
        int? inputSeed = null;
        var allowedIssues = new List<int>();
        string? swarmFocus = null;
        bool metamorphic = false;
        string? compareBase = null;
        bool expectChange = false;
        string? workDirArg = null;
        string? outDirArg = null;
        bool noReduce = false;
        bool noGpuLock = false;
        string repoRoot = Directory.GetCurrentDirectory();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string name = arg;
            string? inlineValue = null;
            int equals = arg.StartsWith("--", StringComparison.Ordinal) ? arg.IndexOf('=') : -1;
            if (equals >= 0)
            {
                name = arg[..equals];
                inlineValue = arg[(equals + 1)..];
            }

            if (name is "--help" or "-h")
            {
                showHelp = true;
                return true;
            }

            switch (name)
            {
                case "--metamorphic":
                    metamorphic = true;
                    continue;
                case "--expect-change":
                    expectChange = true;
                    continue;
                case "--no-reduce":
                    noReduce = true;
                    continue;
                case "--no-gpu-lock":
                    noGpuLock = true;
                    continue;
                case "--profile" or "--minutes" or "--batches" or "--batch-size" or "--fuzz-seed" or "--input-seed"
                    or "--allow-issue" or "--swarm-focus" or "--compare-base" or "--work-dir" or "--out-dir" or "--repo-root":
                    break;
                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }

            string? value = inlineValue;
            if (value is null)
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{name} requires a value.";
                    return false;
                }

                value = args[++i];
            }

            switch (name)
            {
                case "--profile":
                    string? normalized = NormalizeProfile(value);
                    if (normalized is null)
                    {
                        error = $"Invalid --profile '{value}'. Expected managed|cpu|all|M|C|G.";
                        return false;
                    }

                    profile = normalized;
                    break;

                case "--minutes":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) || !double.IsFinite(minutes) || minutes <= 0)
                    {
                        error = "--minutes requires a positive number.";
                        return false;
                    }

                    break;

                case "--batches":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBatches) || parsedBatches <= 0)
                    {
                        error = "--batches requires a positive integer.";
                        return false;
                    }

                    batches = parsedBatches;
                    break;

                case "--batch-size":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out batchSize) || batchSize is < 1 or > 256)
                    {
                        error = "--batch-size requires an integer in [1, 256].";
                        return false;
                    }

                    break;

                case "--fuzz-seed":
                    if (!FuzzRandom.TryParseSeed(value, out fuzzSeed))
                    {
                        error = $"Invalid --fuzz-seed '{value}'.";
                        return false;
                    }

                    break;

                case "--input-seed":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInputSeed))
                    {
                        error = "--input-seed requires an integer.";
                        return false;
                    }

                    inputSeed = parsedInputSeed;
                    break;

                case "--allow-issue":
                    if (!int.TryParse(value.AsSpan().TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int issue) || issue <= 0)
                    {
                        error = "--allow-issue requires a positive integer issue number.";
                        return false;
                    }

                    if (!allowedIssues.Contains(issue))
                    {
                        allowedIssues.Add(issue);
                    }

                    break;

                case "--swarm-focus":
                    if (value.Length == 0 || !FuzzSwarmConfig.TryParseFocus(value, out _))
                    {
                        error = "--swarm-focus requires 'default', 'coalesce-assign-value', or 'tuple-deconstruct-complex'.";
                        return false;
                    }

                    swarmFocus = value;
                    break;

                case "--compare-base":
                    if (value.Length == 0)
                    {
                        error = "--compare-base requires a git ref.";
                        return false;
                    }

                    compareBase = value;
                    break;

                case "--work-dir" or "--out-dir" or "--repo-root":
                    if (value.Length == 0)
                    {
                        error = $"{name} requires a directory path.";
                        return false;
                    }

                    if (name == "--work-dir")
                    {
                        workDirArg = value;
                    }
                    else if (name == "--out-dir")
                    {
                        outDirArg = value;
                    }
                    else
                    {
                        repoRoot = value;
                    }

                    break;
            }
        }

        repoRoot = Path.GetFullPath(repoRoot);
        string workDir = workDirArg is null
            ? Path.Combine(repoRoot, "tmp", "differential-fuzz")
            : Path.GetFullPath(workDirArg, repoRoot);
        string outDir = outDirArg is null
            ? Path.Combine(workDir, "Generated")
            : Path.GetFullPath(outDirArg, repoRoot);

        options = new RunCommandOptions(
            profile, minutes, batches, batchSize, fuzzSeed, inputSeed, allowedIssues, swarmFocus, metamorphic,
            compareBase, expectChange, workDir, outDir, noReduce, noGpuLock, repoRoot);
        return true;
    }

    private static string? NormalizeProfile(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "m" or "managed" => "M",
            "c" or "cpu" or "native" => "C",
            "g" or "all" or "cuda" or "gpu" => "G",
            _ => null,
        };

    // Compares the generated code of --compare-base <ref> and the working tree with `fuzz-diff compare`, which builds
    // both compilers, compares per kernel, reduces and writes findings; its exit code is passed on.
    private static int RunCompare(RunCommandOptions options)
    {
        var args = new List<string>
        {
            "--base", options.CompareBase ?? string.Empty,
            "--batches", (options.Batches ?? DefaultCompareBatches).ToString(CultureInfo.InvariantCulture),
            "--batch-size", options.BatchSize.ToString(CultureInfo.InvariantCulture),
            "--fuzz-seed", FuzzRandom.FormatSeed(options.FuzzSeed),
            "--profile", options.Profile,
            "--work-dir", options.WorkDir,
            "--repo-root", options.RepoRoot,
        };
        if (options.InputSeed is int inputSeed)
        {
            args.Add("--input-seed");
            args.Add(inputSeed.ToString(CultureInfo.InvariantCulture));
        }

        foreach (int issue in options.AllowedIssues)
        {
            args.Add("--allow-issue");
            args.Add(issue.ToString(CultureInfo.InvariantCulture));
        }

        if (options.SwarmFocus is not null)
        {
            args.Add("--swarm-focus");
            args.Add(options.SwarmFocus);
        }

        if (options.Metamorphic)
        {
            args.Add("--metamorphic");
        }

        if (options.ExpectChange)
        {
            args.Add("--expect-change");
        }

        if (options.NoReduce)
        {
            args.Add("--no-reduce");
        }

        return ((IFuzzCommand)CompareCommand.Instance).Run(args.ToArray());
    }
}
