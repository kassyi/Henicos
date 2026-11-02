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
/// Implements the <c>fuzz-diff reduce</c> subcommand.
/// </summary>
public static class ReduceCommand
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Runs the <c>reduce</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args)
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
                    Console.Out.WriteLine(Program.HelpText);
                    return 0;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--source requires a file path.");
                    }

                    sourcePath = args[++i];
                    break;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        return Program.FailUsage("--fuzz-seed requires a 64-bit integer.");
                    }

                    break;

                case "--kernel":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--kernel requires a method name.");
                    }

                    kernelName = args[++i];
                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
                    {
                        return Program.FailUsage("--input-seed requires an integer.");
                    }

                    break;

                case "--profile":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--profile requires M|C|G.");
                    }

                    profile = KnownIssueFilter.NormalizeProfile(args[++i]);
                    break;

                case "--work-dir":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--work-dir requires a directory path.");
                    }

                    workDir = args[++i];
                    break;

                case "--repo-root":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--repo-root requires a directory path.");
                    }

                    repoRoot = args[++i];
                    break;

                case "--test-cmd":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--test-cmd requires a command string.");
                    }

                    testCmd = args[++i];
                    break;

                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--out requires a file path.");
                    }

                    outPath = args[++i];
                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--failing-paths requires a value.");
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--failure-kind requires a value.");
                    }

                    failureKind = args[++i];
                    break;

                case "--no-gpu-lock":
                    noGpuLock = true;
                    break;

                default:
                    return Program.FailUsage($"Unknown option '{arg}'.");
            }
        }

        string initialSource;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            if (!File.Exists(sourcePath))
            {
                return Program.FailUsage($"Source file not found: {sourcePath}");
            }

            initialSource = File.ReadAllText(sourcePath, s_utf8NoBom);
            if (!string.IsNullOrWhiteSpace(kernelName))
            {
                initialSource = IsolateSingleKernelCompilationUnit(initialSource, kernelName);
            }
        }
        else
        {
            var gen = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = fuzzSeed, InputSeed = inputSeed, BatchSize = 64, Profile = profile });
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

        var updated = root.RemoveNodes(others, SyntaxRemoveOptions.KeepNoTrivia);
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
        catch (Exception)
        {
            return null;
        }
    }
}

