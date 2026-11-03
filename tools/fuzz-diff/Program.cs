namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Command-line entry point for <c>tools/fuzz-diff</c>.
/// Dispatches the <c>gen</c>, <c>reduce</c>, <c>dedup</c>, and <c>promote</c> subcommands via an <see cref="IFuzzCommand"/> table.
/// </summary>
public static class Program
{
    internal const string HelpText =
        """
        Usage:
          dotnet run --project tools/fuzz-diff -- <command> [options]

        Commands:
          gen                      Generate a batch of differential fuzz kernels and DifferentialCase definitions.
          reduce                   Reduce a failing kernel (2-stage fast reduction + 16-candidate CUDA batch reduction).
          dedup                    Compute the normalized AST fingerprint of a kernel source file.
          promote                  Emit clean Differential/ repro .cu.cs and DifferentialCase snippet into findings/<fingerprint>/.

        Options for 'gen':
          --fuzz-seed <seed>       64-bit integer seed (decimal or 0x hex) for AST generation (default: 1).
          --input-seed <int>       Optional runtime data input seed (default: DifferentialSeeds.Fixed 0..8).
          --batch-size <n>         Number of kernels per batch (1..256, default: 64).
          --size <1..8>            Complexity scale factor when --batch-size is 1 (default: 4).
          --profile <M|C|G>        Target profile controlling active known-issue avoidance switches (default: C).
          --allow-issue <n>        Re-enable generation of shapes avoided for known issue #<n> (repeatable).
          --swarm-focus <mode>     Optional Swarm focus mode (default, coalesce-assign-value, tuple-deconstruct-complex).
          --out-dir <dir>          Output directory for FuzzBatchKernels.cu.cs and FuzzBatchCases.cs (default: tmp/differential-fuzz/Generated).
          --repo-root <dir>        Repository root path used to emit FuzzBatch.Cpu.csproj and FuzzBatch.Cuda.csproj in the parent work directory.
          --cuda-arch <arch>       Optional single GSharpTargetArchitectures value (e.g. compute_86,sm_86).

        Options for 'reduce':
          --source <file>          Path to a .cu.cs source file containing the failing kernel (or generate via --fuzz-seed / --kernel).
          --fuzz-seed <seed>       64-bit integer fuzz seed (used if --source is omitted or for metadata).
          --kernel <name>          Kernel method name to isolate and reduce when --source has multiple kernels.
          --input-seed <int>       Runtime data input seed that triggers the failure (default: 0).
          --profile <M|C|G>        Reduction profile: M (Managed fast), C (CPU Managed+Native), or G (CUDA 16-candidate batch).
          --work-dir <dir>         Workspace directory containing FuzzBatch.Cpu.csproj / FuzzBatch.Cuda.csproj.
          --repo-root <dir>        Repository root path.
          --test-cmd <cmd>         Optional external shell command returning exit code 0 when the failure reproduces ({source} placeholder).
          --out <file>             Output file path for the reduced .cu.cs source.
          --failing-paths <paths>  Failing paths mask (e.g. "Native,Cuda", default: "Native,Cuda").
          --failure-kind <kind>    Failure kind tag (e.g. "CellMismatch", default: "CellMismatch").
          --column <name>          Failing output column name (default: "hash").

        Options for 'dedup':
          --source <file>          Path to a kernel .cu.cs file.
          --failing-paths <paths>  Failing paths mask (default: "Native,Cuda").
          --failure-kind <kind>    Failure kind tag (default: "CellMismatch").
          --json                   Print output as JSON.

        Options for 'promote':
          --source <file>          Path to the reduced kernel .cu.cs file.
          --fuzz-seed <seed>       64-bit fuzz seed (default: 1).
          --input-seed <int>       Runtime data input seed (default: 0).
          --failing-paths <paths>  Failing paths mask (default: "Native,Cuda").
          --failure-kind <kind>    Failure kind tag (default: "CellMismatch").
          --column <name>          Failing output column name (default: "hash").
          --findings-dir <dir>     Findings root directory (default: tmp/differential-fuzz/findings).
          --kernel <name>          Optional target kernel name inside --source.
        """;

    private static readonly IReadOnlyList<IFuzzCommand> s_commands =
    [
        GenCommand.Instance,
        ReduceCommand.Instance,
        DedupCommand.Instance,
        PromoteCommand.Instance,
    ];

    /// <summary>Gets the registered command table.</summary>
    public static IReadOnlyList<IFuzzCommand> Commands => s_commands;

    /// <summary>Runs the CLI with the specified arguments.</summary>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        string subcommand = args[0];
        foreach (var command in s_commands)
        {
            if (string.Equals(command.Name, subcommand, StringComparison.Ordinal))
            {
                return command.Run(args.AsSpan(1));
            }
        }

        return FailUsage($"Unknown command '{subcommand}'.");
    }

    internal static int FailUsage(string message)
    {
        Console.Error.WriteLine($"fuzz-diff: {message}");
        Console.Error.WriteLine(HelpText);
        return 2;
    }
}
