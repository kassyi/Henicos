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

    /// <summary>Parses command-line arguments into a <see cref="GenCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out GenCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        showHelp = false;
        error = null;

        ulong fuzzSeed = 1UL;
        int? inputSeed = null;
        int batchSize = 64;
        int size = 4;
        string profile = "C";
        var allowedIssues = new HashSet<int>();
        FuzzSwarmFocus swarmFocus = FuzzSwarmFocus.Default;
        bool metamorphic = false;
        string outDir = Path.Combine("tmp", "differential-fuzz", "Generated");
        string? repoRoot = null;
        string? cudaArch = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    showHelp = true;
                    return true;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        error = "--fuzz-seed requires a valid 64-bit integer (decimal or 0x hex).";
                        return false;
                    }

                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInputSeed))
                    {
                        error = "--input-seed requires a 32-bit integer.";
                        return false;
                    }

                    inputSeed = parsedInputSeed;
                    break;

                case "--batch-size":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out batchSize) || batchSize is < 1 or > 256)
                    {
                        error = "--batch-size requires an integer in [1, 256].";
                        return false;
                    }

                    break;

                case "--size":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size is < 1 or > 8)
                    {
                        error = "--size requires an integer in [1, 8].";
                        return false;
                    }

                    break;

                case "--profile":
                    if (i + 1 >= args.Length)
                    {
                        error = "--profile requires M|C|G.";
                        return false;
                    }

                    profile = KnownIssueFilter.NormalizeProfile(args[++i]);
                    break;

                case "--allow-issue":
                    if (i + 1 >= args.Length ||
                        !int.TryParse(args[++i].AsSpan().TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int issueNum) ||
                        issueNum <= 0)
                    {
                        error = "--allow-issue requires a positive integer issue number.";
                        return false;
                    }

                    allowedIssues.Add(issueNum);
                    break;

                case "--swarm-focus":
                    if (i + 1 >= args.Length || !FuzzSwarmConfig.TryParseFocus(args[++i], out swarmFocus))
                    {
                        error = "--swarm-focus requires 'default', 'coalesce-assign-value', or 'tuple-deconstruct-complex'.";
                        return false;
                    }

                    break;

                case "--metamorphic":
                    metamorphic = true;
                    break;

                case "--out-dir":
                    if (i + 1 >= args.Length)
                    {
                        error = "--out-dir requires a directory path.";
                        return false;
                    }

                    outDir = args[++i];
                    break;

                case "--repo-root":
                    if (i + 1 >= args.Length)
                    {
                        error = "--repo-root requires a directory path.";
                        return false;
                    }

                    repoRoot = args[++i];
                    break;

                case "--cuda-arch":
                    if (i + 1 >= args.Length)
                    {
                        error = "--cuda-arch requires a value.";
                        return false;
                    }

                    cudaArch = args[++i];
                    break;

                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        var fuzzOptions = new FuzzOptions
        {
            FuzzSeed = fuzzSeed,
            InputSeed = inputSeed,
            BatchSize = batchSize,
            Size = size,
            Profile = profile,
            AllowedIssues = allowedIssues,
            SwarmFocus = swarmFocus,
            Metamorphic = metamorphic,
        };

        options = new GenCommandOptions(fuzzOptions, outDir, repoRoot, cudaArch);
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
