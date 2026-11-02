using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Command-line entry point for <c>tools/fuzz-diff</c>.
/// Supports the <c>gen</c>, <c>reduce</c>, <c>dedup</c>, and <c>promote</c> subcommands.
/// </summary>
public static class Program
{
    private const string HelpText =
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
          --findings-dir <dir>     Findings root directory (default: tmp/differential-fuzz/findings).
          --kernel <name>          Optional target kernel name inside --source.
        """;

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
            "reduce" => RunReduce(args.AsSpan(1)),
            "dedup" => RunDedup(args.AsSpan(1)),
            "promote" => RunPromote(args.AsSpan(1)),
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

    private static int RunReduce(ReadOnlySpan<string> args)
    {
        string? sourcePath = null;
        ulong fuzzSeed = 1UL;
        string? kernelName = null;
        int inputSeed = 0;
        string profile = "C";
        string workDir = Path.Combine("tmp", "differential-fuzz");
        string? repoRoot = null;
        string? testCmd = null;
        string? outPath = null;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        bool noGpuLock = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.WriteLine(HelpText);
                    return 0;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--source requires a file path.");
                    }

                    sourcePath = args[++i];
                    break;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        return FailUsage("--fuzz-seed requires a 64-bit integer.");
                    }

                    break;

                case "--kernel":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--kernel requires a method name.");
                    }

                    kernelName = args[++i];
                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
                    {
                        return FailUsage("--input-seed requires an integer.");
                    }

                    break;

                case "--profile":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--profile requires M|C|G.");
                    }

                    profile = NormalizeProfile(args[++i]);
                    break;

                case "--work-dir":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--work-dir requires a directory path.");
                    }

                    workDir = args[++i];
                    break;

                case "--repo-root":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--repo-root requires a directory path.");
                    }

                    repoRoot = args[++i];
                    break;

                case "--test-cmd":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--test-cmd requires a command string.");
                    }

                    testCmd = args[++i];
                    break;

                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--out requires a file path.");
                    }

                    outPath = args[++i];
                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--failing-paths requires a value.");
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--failure-kind requires a value.");
                    }

                    failureKind = args[++i];
                    break;

                case "--no-gpu-lock":
                    noGpuLock = true;
                    break;

                default:
                    return FailUsage($"Unknown option '{arg}'.");
            }
        }

        string initialSource;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            if (!File.Exists(sourcePath))
            {
                return FailUsage($"Source file not found: {sourcePath}");
            }

            initialSource = File.ReadAllText(sourcePath, s_utf8NoBom);
            if (!string.IsNullOrWhiteSpace(kernelName))
            {
                initialSource = IsolateSingleKernelCompilationUnit(initialSource, kernelName);
            }
        }
        else
        {
            var gen = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = fuzzSeed, InputSeed = inputSeed, BatchSize = 64 });
            var batch = gen.GenerateBatch();
            string target = kernelName ?? batch.Kernels[0].KernelName;
            initialSource = IsolateSingleKernelCompilationUnit(batch.KernelsSource, target);
        }

        string reducedSource;
        int origStmts = FuzzReducer.CountKernelExecutableStatements(initialSource);
        int iterations = 0;

        if (!string.IsNullOrWhiteSpace(testCmd))
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"fuzz_reduce_{Guid.NewGuid():N}.cu.cs");
            try
            {
                var res = FuzzReducer.Reduce(initialSource, candidate =>
                {
                    File.WriteAllText(tempFile, candidate, s_utf8NoBom);
                    string cmd = testCmd.Replace("{source}", tempFile, StringComparison.Ordinal);
                    return RunExternalShellPredicate(cmd);
                });
                reducedSource = res.ReducedSource;
                iterations = res.Iterations;
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        else if (profile == "G")
        {
            // Batched CUDA reduction (16-candidate speculative batch reduction)
            (reducedSource, iterations) = ReduceCudaWithSpeculativeBatches(
                initialSource,
                fuzzSeed,
                inputSeed,
                workDir,
                repoRoot,
                noGpuLock);
        }
        else
        {
            // Single-candidate Managed / CPU reduction
            var res = FuzzReducer.Reduce(initialSource, candidate =>
                EvaluateSingleCandidateInWorkspace(candidate, fuzzSeed, inputSeed, profile, workDir, repoRoot));
            reducedSource = res.ReducedSource;
            iterations = res.Iterations;
        }

        if (!string.IsNullOrWhiteSpace(outPath))
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outPath, reducedSource, s_utf8NoBom);
        }

        var fp = FuzzDeduplicator.ComputeFingerprint(reducedSource, failingPaths, failureKind);
        int finalStmts = FuzzReducer.CountKernelExecutableStatements(reducedSource);

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            status = "reduced",
            fingerprint = fp.Fingerprint,
            originalStatements = origStmts,
            reducedStatements = finalStmts,
            iterations,
            failingPaths = fp.FailingPathsMask,
            failureKind = fp.FailureKindTag,
            outPath = outPath is not null ? Path.GetFullPath(outPath) : null,
        }));
        return 0;
    }

    private static int RunDedup(ReadOnlySpan<string> args)
    {
        string? sourcePath = null;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        bool json = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.WriteLine(HelpText);
                    return 0;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--source requires a file path.");
                    }

                    sourcePath = args[++i];
                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--failing-paths requires a value.");
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--failure-kind requires a value.");
                    }

                    failureKind = args[++i];
                    break;

                case "--json":
                    json = true;
                    break;

                default:
                    return FailUsage($"Unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return FailUsage("--source requires an existing .cu.cs file.");
        }

        string source = File.ReadAllText(sourcePath, s_utf8NoBom);
        var fp = FuzzDeduplicator.ComputeFingerprint(source, failingPaths, failureKind);
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                fingerprint = fp.Fingerprint,
                hash64 = string.Create(CultureInfo.InvariantCulture, $"0x{fp.Hash64:X16}"),
                failingPaths = fp.FailingPathsMask,
                failureKind = fp.FailureKindTag,
                normalizedAstSkeleton = fp.NormalizedAstSkeleton,
            }));
        }
        else
        {
            Console.Out.WriteLine(fp.Fingerprint);
        }

        return 0;
    }

    private static int RunPromote(ReadOnlySpan<string> args)
    {
        string? sourcePath = null;
        ulong fuzzSeed = 1UL;
        int inputSeed = 0;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        string findingsDir = Path.Combine("tmp", "differential-fuzz", "findings");
        string? kernelName = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.WriteLine(HelpText);
                    return 0;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--source requires a file path.");
                    }

                    sourcePath = args[++i];
                    break;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        return FailUsage("--fuzz-seed requires a 64-bit integer.");
                    }

                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
                    {
                        return FailUsage("--input-seed requires an integer.");
                    }

                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--failing-paths requires a value.");
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--failure-kind requires a value.");
                    }

                    failureKind = args[++i];
                    break;

                case "--findings-dir":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--findings-dir requires a directory path.");
                    }

                    findingsDir = args[++i];
                    break;

                case "--kernel":
                    if (i + 1 >= args.Length)
                    {
                        return FailUsage("--kernel requires a kernel method name.");
                    }

                    kernelName = args[++i];
                    break;

                default:
                    return FailUsage($"Unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return FailUsage("--source requires an existing .cu.cs file.");
        }

        string source = File.ReadAllText(sourcePath, s_utf8NoBom);
        var artifacts = FuzzPromoter.BuildPromotionArtifacts(
            source,
            fuzzSeed,
            inputSeed,
            failingPaths,
            failureKind,
            targetKernelName: kernelName);
        string writtenPath = FuzzPromoter.WritePromotedFinding(findingsDir, artifacts, fuzzSeed);

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            status = "promoted",
            fingerprint = artifacts.Fingerprint,
            reproKernelPath = writtenPath,
            reproClassName = artifacts.ReproKernelClassName,
            reproMethodName = artifacts.ReproKernelMethodName,
        }));
        return 0;
    }

    private static string IsolateSingleKernelCompilationUnit(string source, string targetKernelName)
    {
        var root = FuzzReducer.ParseRoot(source);
        var kernels = FuzzReducer.GetKernelMethods(root);
        var selected = kernels.FirstOrDefault(m => string.Equals(m.Identifier.ValueText, targetKernelName, StringComparison.Ordinal));
        if (selected is null)
        {
            return source;
        }

        var others = kernels.Where(m => m != selected).ToList();
        if (others.Count == 0)
        {
            return source;
        }

        var updated = root.RemoveNodes(others, Microsoft.CodeAnalysis.SyntaxRemoveOptions.KeepNoTrivia);
        return updated?.ToFullString() ?? source;
    }

    private static (string ReducedSource, int Iterations) ReduceCudaWithSpeculativeBatches(
        string initialSource,
        ulong fuzzSeed,
        int inputSeed,
        string workDir,
        string? repoRoot,
        bool noGpuLock)
    {
        string current = FuzzReducer.HoistLocalsToDefault(initialSource);
        int iterations = 0;
        IReadOnlyList<IReadOnlyList<int>>? priorSuccessRemovals = null;

        for (int round = 0; round < 7; round++)
        {
            var specBatch = FuzzReducer.GenerateSpeculativeBatchCandidates(
                current,
                fuzzSeed,
                inputSeed,
                maxCandidates: 16,
                priorSuccessfulRemovals: priorSuccessRemovals);

            iterations++;
            var failingCandidateNames = EvaluateSpeculativeBatchInWorkspace(
                specBatch,
                workDir,
                repoRoot,
                useCuda: true,
                noGpuLock);

            var winningRemovals = new List<IReadOnlyList<int>>();
            foreach (var cand in specBatch.Candidates)
            {
                if (failingCandidateNames.Contains(cand.CandidateKernelName) && cand.RemovedStatementIndices.Count > 0)
                {
                    winningRemovals.Add(cand.RemovedStatementIndices);
                }
            }

            var best = FuzzReducer.SelectBestSpeculativeCandidate(
                specBatch,
                cand => failingCandidateNames.Contains(cand.CandidateKernelName));

            if (best is null)
            {
                break;
            }

            string updatedSource = ReplaceKernelMethodInUnit(current, best.CandidateKernelMethodSource);
            if (string.Equals(updatedSource, current, StringComparison.Ordinal))
            {
                break;
            }

            current = updatedSource;
            priorSuccessRemovals = winningRemovals.Count >= 2 ? winningRemovals : null;
        }

        // Deterministically strip any hoisted `T v = default(T);` declarations that are no longer referenced anywhere in the reduced method body.
        current = FuzzReducer.PruneUnusedHoistedLocals(current, static _ => true);

        return (current, iterations);
    }

    private static string ReplaceKernelMethodInUnit(string unitSource, string newMethodSource)
    {
        var root = FuzzReducer.ParseRoot(unitSource);
        var oldKernel = FuzzReducer.GetKernelMethods(root).FirstOrDefault();
        if (oldKernel is null)
        {
            return unitSource;
        }

        var parsedNewMethod = CSharpSyntaxTree.ParseText($"class __C {{ {newMethodSource} }}")
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First()
            .WithIdentifier(oldKernel.Identifier);

        return root.ReplaceNode(oldKernel, parsedNewMethod).ToFullString();
    }

    private static bool EvaluateSingleCandidateInWorkspace(
        string candidateSource,
        ulong fuzzSeed,
        int inputSeed,
        string profile,
        string workDir,
        string? repoRoot)
    {
        var root = FuzzReducer.ParseRoot(candidateSource);
        var kernel = FuzzReducer.GetKernelMethods(root).FirstOrDefault();
        if (kernel is null)
        {
            return false;
        }

        var specBatch = FuzzReducer.GenerateSpeculativeBatchCandidates(
            candidateSource,
            fuzzSeed,
            inputSeed,
            maxCandidates: 1);

        // Replace Candidate_00 with the exact candidate kernel body.
        var exactMethod = kernel.WithIdentifier(SyntaxFactory.Identifier("Candidate_00"));
        var exactCand = specBatch.Candidates[0] with
        {
            CandidateKernelName = "Candidate_00",
            CandidateKernelMethodSource = exactMethod.ToFullString(),
        };
        var singleBatch = specBatch with
        {
            KernelsSource = candidateSource.Replace($"void {kernel.Identifier.ValueText}(", "void Candidate_00(", StringComparison.Ordinal),
            Candidates = [exactCand],
        };

        var failing = EvaluateSpeculativeBatchInWorkspace(
            singleBatch,
            workDir,
            repoRoot,
            useCuda: profile == "G",
            noGpuLock: true,
            managedOnly: profile == "M");

        return failing.Contains("Candidate_00");
    }

    private static HashSet<string> EvaluateSpeculativeBatchInWorkspace(
        SpeculativeCandidateBatch batch,
        string workDir,
        string? repoRoot,
        bool useCuda,
        bool noGpuLock,
        bool managedOnly = false)
    {
        var failing = new HashSet<string>(StringComparer.Ordinal);
        string fullWorkDir = Path.GetFullPath(workDir);
        string genDir = Path.Combine(fullWorkDir, "Generated");
        Directory.CreateDirectory(genDir);

        File.WriteAllText(Path.Combine(genDir, "FuzzBatchKernels.cu.cs"), batch.KernelsSource, s_utf8NoBom);
        File.WriteAllText(Path.Combine(genDir, "FuzzBatchCases.cs"), batch.CasesSource, s_utf8NoBom);

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string? cudaArch = useCuda ? DetectHostCudaArchForReducer() : null;
            FuzzBatchWorkspaceWriter.WriteProjectScaffolds(fullWorkDir, genDir, Path.GetFullPath(repoRoot), cudaArch);
        }

        string hostOs = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unix";
        string projName = useCuda ? "FuzzBatch.Cuda" : "FuzzBatch.Cpu";
        string csproj = Path.Combine(fullWorkDir, $"{projName}.csproj");
        string dll = Path.Combine(fullWorkDir, "bin", hostOs, projName, "Debug", "net10.0", $"{projName}.dll");

        string skipCpu = (!useCuda && managedOnly) ? "-p:GSharpSkipCpuCompilation=true" : "-p:GSharpSkipCpuCompilation=false";
        string assetsPath = Path.Combine(fullWorkDir, "obj", hostOs, projName, "project.assets.json");
        string noRestore = File.Exists(assetsPath) ? "--no-restore" : string.Empty;
        int buildCode = RunDotnetProcess($"build \"{csproj}\" -c Debug -m:1 -nodeReuse:false --no-dependencies {noRestore} {skipCpu}", out _, out _);
        if (buildCode != 0)
        {
            return failing;
        }

        string runArgs = $"exec \"{dll}\"";
        if (managedOnly)
        {
            runArgs += " --managed-only";
        }

        if (batch.InputSeed is { } s)
        {
            runArgs += string.Create(CultureInfo.InvariantCulture, $" --input-seed {s}");
        }

        string stdout;
        if (useCuda && !noGpuLock && !string.IsNullOrWhiteSpace(repoRoot))
        {
            string lockScript = Path.Combine(Path.GetFullPath(repoRoot), "scripts", "agent", "gpu-lock.js");
            RunProcess("node", $"\"{lockScript}\" -- \"dotnet {runArgs}\"", out stdout, out _);
        }
        else
        {
            RunDotnetProcess(runArgs, out stdout, out _);
        }

        foreach (string line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("status", out var statusProp) &&
                    statusProp.GetString() == "failed" &&
                    doc.RootElement.TryGetProperty("kernel", out var kernelProp) &&
                    kernelProp.GetString() is { } kName)
                {
                    failing.Add(kName);
                }
            }
            catch (JsonException)
            {
                // Ignore non-event JSON lines.
            }
        }

        return failing;
    }

    private static bool RunExternalShellPredicate(string command)
    {
        string shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        string shellArgs = OperatingSystem.IsWindows() ? $"/d /s /c \"{command}\"" : $"-c \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        int code = RunProcess(shell, shellArgs, out _, out _);
        return code == 0;
    }

    private static int RunDotnetProcess(string arguments, out string stdout, out string stderr) =>
        RunProcess("dotnet", arguments, out stdout, out stderr);

    private static int RunProcess(string fileName, string arguments, out string stdout, out string stderr)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        stdout = proc.StandardOutput.ReadToEnd();
        stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static string NormalizeProfile(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "M" or "MANAGED" => "M",
        "G" or "ALL" or "CUDA" or "GPU" => "G",
        _ => "C",
    };

    private static string? DetectHostCudaArchForReducer()
    {
        try
        {
            int code = RunProcess("nvidia-smi", "--query-gpu=compute_cap --format=csv,noheader", out string stdout, out _);
            if (code != 0)
            {
                return null;
            }

            string? firstLine = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(firstLine) || !System.Text.RegularExpressions.Regex.IsMatch(firstLine, @"^\d+\.\d+$"))
            {
                return null;
            }

            string digits = firstLine.Replace(".", string.Empty, StringComparison.Ordinal);
            return $"compute_{digits},sm_{digits}";
        }
        catch
        {
            return null;
        }
    }

    private static int FailUsage(string message)
    {
        Console.Error.WriteLine($"fuzz-diff: {message}");
        Console.Error.WriteLine(HelpText);
        return 2;
    }
}
