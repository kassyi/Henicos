using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Command-line entry point for <c>tools/fuzz-diff</c>.
/// Supports the <c>gen</c> subcommand to generate differential fuzz kernel batches.
/// </summary>
public static class Program
{
    private const string HelpText =
        """
        Usage:
          dotnet run --project tools/fuzz-diff -- gen [options]

        Commands:
          gen                      Generate a batch of differential fuzz kernels and DifferentialCase definitions.

        Options for 'gen':
          --fuzz-seed <seed>       64-bit integer seed (decimal or 0x hex) for AST generation (default: 1).
          --input-seed <int>       Optional runtime data input seed (default: DifferentialSeeds.Fixed 0..8).
          --batch-size <n>         Number of kernels per batch (1..256, default: 64).
          --size <1..8>            Complexity scale factor when --batch-size is 1 (default: 4).
          --out-dir <dir>          Output directory for FuzzBatchKernels.cu.cs and FuzzBatchCases.cs (default: tmp/differential-fuzz/Generated).
          --repo-root <dir>        Repository root path used to emit FuzzBatch.Cpu.csproj and FuzzBatch.Cuda.csproj in the parent work directory.
          --cuda-arch <arch>       Optional single GSharpTargetArchitectures value (e.g. compute_86,sm_86).
          --help, -h               Show this help text.
        """;

    /// <summary>Runs the CLI with the specified arguments.</summary>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        string subcommand = args[0];
        return subcommand switch
        {
            "gen" => RunGen(args.AsSpan(1)),
            _ => FailUsage($"Unknown command '{subcommand}'."),
        };
    }

    private static int RunGen(ReadOnlySpan<string> args)
    {
        ulong fuzzSeed = 1UL;
        int? inputSeed = null;
        int batchSize = 64;
        int size = 4;
        string outDir = Path.Combine("tmp", "differential-fuzz", "Generated");
        string? repoRoot = null;
        string? cudaArch = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.WriteLine(HelpText);
                    return 0;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        return FailUsage("--fuzz-seed requires a valid 64-bit integer (decimal or 0x hex).");
                    }

                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInputSeed))
                    {
                        return FailUsage("--input-seed requires a 32-bit integer.");
                    }

                    inputSeed = parsedInputSeed;
                    break;

                case "--batch-size":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out batchSize) || batchSize is < 1 or > 256)
                    {
                        return FailUsage("--batch-size requires an integer in [1, 256].");
                    }

                    break;

                case "--size":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size is < 1 or > 8)
                    {
                        return FailUsage("--size requires an integer in [1, 8].");
                    }

                    break;

                case "--out-dir":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--out-dir requires a directory path.");
                    }

                    outDir = args[++i];
                    break;

                case "--repo-root":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--repo-root requires a directory path.");
                    }

                    repoRoot = args[++i];
                    break;

                case "--cuda-arch":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--cuda-arch requires a value.");
                    }

                    cudaArch = args[++i];
                    break;

                default:
                    return FailUsage($"Unknown option '{arg}'.");
            }
        }

        var options = new FuzzOptions
        {
            FuzzSeed = fuzzSeed,
            InputSeed = inputSeed,
            BatchSize = batchSize,
            Size = size,
        };

        var generator = new ScalarFuzzGenerator(options);
        var batch = generator.GenerateBatch();
        FuzzBatchWorkspaceWriter.WriteBatch(batch, outDir, repoRoot, cudaArch);

        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"fuzz-diff gen: wrote {batch.Kernels.Count} kernels (fuzz-seed={FuzzRandom.FormatSeed(fuzzSeed)}) to {Path.GetFullPath(outDir)}"));
        return 0;
    }

    private static int FailUsage(string message)
    {
        Console.Error.WriteLine($"fuzz-diff: {message}");
        Console.Error.WriteLine(HelpText);
        return 2;
    }
}
