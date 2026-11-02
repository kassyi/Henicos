using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Implements the <c>fuzz-diff gen</c> subcommand.
/// </summary>
public static class GenCommand
{
    /// <summary>Runs the <c>gen</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args)
    {
        ulong fuzzSeed = 1UL;
        int? inputSeed = null;
        int batchSize = 64;
        int size = 4;
        string profile = "C";
        var allowedIssues = new HashSet<int>();
        FuzzSwarmFocus swarmFocus = FuzzSwarmFocus.Default;
        string outDir = Path.Combine("tmp", "differential-fuzz", "Generated");
        string? repoRoot = null;
        string? cudaArch = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.WriteLine(Program.HelpText);
                    return 0;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        return Program.FailUsage("--fuzz-seed requires a valid 64-bit integer (decimal or 0x hex).");
                    }

                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInputSeed))
                    {
                        return Program.FailUsage("--input-seed requires a 32-bit integer.");
                    }

                    inputSeed = parsedInputSeed;
                    break;

                case "--batch-size":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out batchSize) || batchSize is < 1 or > 256)
                    {
                        return Program.FailUsage("--batch-size requires an integer in [1, 256].");
                    }

                    break;

                case "--size":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size is < 1 or > 8)
                    {
                        return Program.FailUsage("--size requires an integer in [1, 8].");
                    }

                    break;

                case "--profile":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--profile requires M|C|G.");
                    }

                    profile = KnownIssueFilter.NormalizeProfile(args[++i]);
                    break;

                case "--allow-issue":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i].TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int issueNum) || issueNum <= 0)
                    {
                        return Program.FailUsage("--allow-issue requires a positive integer issue number.");
                    }

                    allowedIssues.Add(issueNum);
                    break;

                case "--swarm-focus":
                    if (i + 1 >= args.Length || !FuzzSwarmConfig.TryParseFocus(args[++i], out swarmFocus))
                    {
                        return Program.FailUsage("--swarm-focus requires 'default', 'coalesce-assign-value', or 'tuple-deconstruct-complex'.");
                    }

                    break;

                case "--out-dir":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--out-dir requires a directory path.");
                    }

                    outDir = args[++i];
                    break;

                case "--repo-root":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--repo-root requires a directory path.");
                    }

                    repoRoot = args[++i];
                    break;

                case "--cuda-arch":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--cuda-arch requires a value.");
                    }

                    cudaArch = args[++i];
                    break;

                default:
                    return Program.FailUsage($"Unknown option '{arg}'.");
            }
        }

        var options = new FuzzOptions
        {
            FuzzSeed = fuzzSeed,
            InputSeed = inputSeed,
            BatchSize = batchSize,
            Size = size,
            Profile = profile,
            AllowedIssues = allowedIssues,
            SwarmFocus = swarmFocus,
        };

        var generator = new ScalarFuzzGenerator(options);
        var batch = generator.GenerateBatch();
        FuzzBatchWorkspaceWriter.WriteBatch(batch, outDir, repoRoot, cudaArch);

        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"fuzz-diff gen: wrote {batch.Kernels.Count} kernels (fuzz-seed={FuzzRandom.FormatSeed(fuzzSeed)}, profile={profile}) to {Path.GetFullPath(outDir)}"));
        return 0;
    }
}

