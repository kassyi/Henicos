using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Workspace-backed speculative-batch reduction runner used by <see cref="ReduceCommand"/>.
/// </summary>
internal static class FuzzWorkspaceReducer
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private sealed record class WorkspaceReductionContext(
        ulong FuzzSeed,
        int InputSeed,
        string Profile,
        string WorkDir,
        string? RepoRoot,
        FuzzFailureSignature ExpectedSignature,
        bool NoGpuLock);

    private sealed record class BatchRunConfig(
        string FullWorkDir,
        string? RepoRoot,
        SpeculativeCandidateBatch Batch,
        bool IsCudaProject,
        bool NoGpuLock,
        bool ManagedOnly,
        FuzzFailureSignature ExpectedSignature);

    private readonly record struct FuzzEventResult(
        string KernelName,
        List<string> Paths,
        string FailureKind,
        string Column);

    internal static (string ReducedSource, int Iterations) ReduceWorkspaceWithSpeculativeBatches(
        string initialSource,
        ulong fuzzSeed,
        int inputSeed,
        string profile,
        string workDir,
        string? repoRoot,
        FuzzFailureSignature expectedSignature,
        bool noGpuLock)
    {
        var ctx = new WorkspaceReductionContext(
            fuzzSeed, inputSeed, profile, workDir, repoRoot, expectedSignature, noGpuLock);
        int iterations = 0;
        var (afterHoisting, hoistIter) = TryApplyInitialHoisting(initialSource, ctx);
        iterations += hoistIter;

        var (afterRounds, roundIter) = RunSpeculativeRounds(afterHoisting, ctx);
        iterations += roundIter;

        var (afterPruning, pruneIter) = TryPruneUnusedHoistedLocals(afterRounds, ctx);
        iterations += pruneIter;

        return (afterPruning, iterations);
    }

    private static (string Current, int Iterations) TryApplyInitialHoisting(
        string current,
        WorkspaceReductionContext ctx)
    {
        string hoisted = FuzzReducer.HoistLocalsToDefault(current);
        if (string.Equals(hoisted, current, StringComparison.Ordinal))
        {
            return (current, 0);
        }

        bool reproduced = EvaluateSingleCandidateInWorkspace(
            hoisted,
            ctx.FuzzSeed,
            ctx.InputSeed,
            ctx.Profile,
            ctx.WorkDir,
            ctx.RepoRoot,
            ctx.ExpectedSignature,
            ctx.NoGpuLock);
        return (reproduced ? hoisted : current, 1);
    }

    private static (string Current, int Iterations) RunSpeculativeRounds(
        string current,
        WorkspaceReductionContext ctx)
    {
        int iterations = 0;
        IReadOnlyList<IReadOnlyList<int>>? priorSuccessRemovals = null;
        for (int round = 0; round < 14; round++)
        {
            var specBatch = FuzzReducer.GenerateSpeculativeBatchCandidates(
                current,
                ctx.FuzzSeed,
                ctx.InputSeed,
                maxCandidates: 16,
                priorSuccessfulRemovals: priorSuccessRemovals);

            iterations++;
            var failingCandidateNames = EvaluateSpeculativeBatchInWorkspace(
                specBatch,
                ctx.WorkDir,
                ctx.RepoRoot,
                useCuda: ctx.Profile == "G",
                ctx.NoGpuLock,
                ctx.ExpectedSignature,
                managedOnly: ctx.Profile == "M");

            var winningRemovals = CollectWinningRemovals(specBatch, failingCandidateNames);
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

        return (current, iterations);
    }

    private static List<IReadOnlyList<int>> CollectWinningRemovals(
        SpeculativeCandidateBatch specBatch,
        IReadOnlySet<string> failingCandidateNames)
    {
        var winningRemovals = new List<IReadOnlyList<int>>();
        foreach (var cand in specBatch.Candidates)
        {
            if (failingCandidateNames.Contains(cand.CandidateKernelName) && cand.RemovedStatementIndices.Count > 0)
            {
                winningRemovals.Add(cand.RemovedStatementIndices);
            }
        }

        return winningRemovals;
    }

    private static (string Current, int Iterations) TryPruneUnusedHoistedLocals(
        string current,
        WorkspaceReductionContext ctx)
    {
        string allPruned = FuzzReducer.PruneUnusedHoistedLocals(current, static _ => true);
        if (string.Equals(allPruned, current, StringComparison.Ordinal))
        {
            return (current, 0);
        }

        if (EvaluateSingleCandidateInWorkspace(
            allPruned,
            ctx.FuzzSeed,
            ctx.InputSeed,
            ctx.Profile,
            ctx.WorkDir,
            ctx.RepoRoot,
            ctx.ExpectedSignature,
            ctx.NoGpuLock))
        {
            return (allPruned, 1);
        }

        int iter = 1;
        string pruned = FuzzReducer.PruneUnusedHoistedLocals(
            current,
            cand =>
            {
                iter++;
                return EvaluateSingleCandidateInWorkspace(
                    cand,
                    ctx.FuzzSeed,
                    ctx.InputSeed,
                    ctx.Profile,
                    ctx.WorkDir,
                    ctx.RepoRoot,
                    ctx.ExpectedSignature,
                    ctx.NoGpuLock);
            });
        return (pruned, iter);
    }

    internal static bool RunExternalShellPredicate(string command)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/d /s /c \"{command}\"")
            : CreateUnixShellStartInfo(command);
        int code = FuzzRunProcess.RunWithStartInfo(psi, out _, out _, out bool timedOut, timeoutMs: 120_000);
        return !timedOut && code == 0;
    }

    internal static string ReplaceKernelMethodInUnit(string unitSource, string newMethodSource)
    {
        var root = FuzzReducer.ParseRoot(unitSource);
        var kernels = FuzzReducer.GetKernelMethods(root);
        if (kernels.Count == 0)
        {
            return unitSource;
        }

        var oldKernel = kernels[0];
        MethodDeclarationSyntax? parsedNewMethod = null;
        foreach (var node in CSharpSyntaxTree.ParseText($"class __C {{ {newMethodSource} }}").GetRoot().DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method)
            {
                parsedNewMethod = method
                    .WithIdentifier(oldKernel.Identifier)
                    .WithLeadingTrivia(oldKernel.GetLeadingTrivia());
                break;
            }
        }

        if (parsedNewMethod is null)
        {
            return unitSource;
        }

        return FuzzSyntaxHelpers.RenderFullNode(root.ReplaceNode(oldKernel, parsedNewMethod));
    }

    private static bool EvaluateSingleCandidateInWorkspace(
        string candidateSource,
        ulong fuzzSeed,
        int inputSeed,
        string profile,
        string workDir,
        string? repoRoot,
        FuzzFailureSignature expectedSignature,
        bool noGpuLock)
    {
        var root = FuzzReducer.ParseRoot(candidateSource);
        var kernels = FuzzReducer.GetKernelMethods(root);
        if (kernels.Count == 0)
        {
            return false;
        }

        var singleBatch = BuildSingleCandidateBatch(candidateSource, root, kernels[0], fuzzSeed, inputSeed);
        var failing = EvaluateSpeculativeBatchInWorkspace(
            singleBatch,
            workDir,
            repoRoot,
            useCuda: profile == "G",
            noGpuLock: noGpuLock,
            expectedSignature: expectedSignature,
            managedOnly: profile == "M");

        return failing.Contains("Candidate_00");
    }

    // The candidate kernel alone, renamed Candidate_00, with its case and the oracle twins of its split methods
    private static SpeculativeCandidateBatch BuildSingleCandidateBatch(
        string candidateSource,
        CompilationUnitSyntax root,
        MethodDeclarationSyntax kernel,
        ulong fuzzSeed,
        int inputSeed)
    {
        var specBatch = FuzzReducer.GenerateSpeculativeBatchCandidates(
            candidateSource,
            fuzzSeed,
            inputSeed,
            maxCandidates: 1);

        var exactMethod = kernel.WithIdentifier(SyntaxFactory.Identifier("Candidate_00"));
        var exactCand = specBatch.Candidates[0] with
        {
            CandidateKernelName = "Candidate_00",
            CandidateKernelMethodSource = FuzzSyntaxHelpers.RenderFullNode(exactMethod),
            CandidateCasePropertySource = FuzzSpeculativeBatchBuilder.RenderCandidateCaseProperty(
                exactMethod,
                FuzzReducer.GetDeviceFunctionMethods(root),
                fuzzSeed,
                inputSeed),
        };
        return specBatch with
        {
            KernelsSource = FuzzSplitOracle.WithOracleTwins(
                FuzzSyntaxHelpers.RenderFullNode(root.ReplaceNode(kernel, exactMethod))),
            CasesSource = FuzzSpeculativeBatchBuilder.BuildSpeculativeCasesCompilationUnit([exactCand], fuzzSeed),
            Candidates = [exactCand],
        };
    }

    private static HashSet<string> EvaluateSpeculativeBatchInWorkspace(
        SpeculativeCandidateBatch batch,
        string workDir,
        string? repoRoot,
        bool useCuda,
        bool noGpuLock,
        FuzzFailureSignature expectedSignature,
        bool managedOnly = false)
    {
        string fullWorkDir = Path.GetFullPath(workDir);
        WriteSpeculativeBatchArtifacts(fullWorkDir, repoRoot, batch, useCuda);

        var perKernelFailures = new Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)>(
            StringComparer.Ordinal);
        var baseConfig = new BatchRunConfig(
            fullWorkDir, repoRoot, batch, IsCudaProject: false, noGpuLock, managedOnly, expectedSignature);
        RunSpeculativePasses(baseConfig, useCuda, perKernelFailures);

        return CollectMatchingFailingCandidates(perKernelFailures, expectedSignature);
    }

    private static void WriteSpeculativeBatchArtifacts(
        string fullWorkDir,
        string? repoRoot,
        SpeculativeCandidateBatch batch,
        bool useCuda)
    {
        string genDir = Path.Combine(fullWorkDir, "Generated");
        Directory.CreateDirectory(genDir);

        File.WriteAllText(Path.Combine(genDir, "FuzzBatchKernels.cu.cs"), batch.KernelsSource, s_utf8NoBom);
        File.WriteAllText(Path.Combine(genDir, "FuzzBatchCases.cs"), batch.CasesSource, s_utf8NoBom);

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string? cudaArch = useCuda ? DetectHostCudaArchForReducer() : null;
            FuzzBatchWorkspaceWriter.WriteProjectScaffolds(fullWorkDir, genDir, Path.GetFullPath(repoRoot), cudaArch);
        }
    }

    private static void RunSpeculativePasses(
        BatchRunConfig baseConfig,
        bool useCuda,
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures)
    {
        var mask = baseConfig.ExpectedSignature.FailingPathsMask;
        bool hasManaged = mask.Contains("Managed", StringComparison.OrdinalIgnoreCase);
        bool hasNative = mask.Contains("Native", StringComparison.OrdinalIgnoreCase);
        bool needsCpuPass = !useCuda || hasManaged || hasNative;

        if (needsCpuPass)
        {
            var cpuConfig = baseConfig with { IsCudaProject = false, NoGpuLock = true };
            RunBatchProjectAndCollectEvents(cpuConfig, perKernelFailures);
        }

        if (useCuda)
        {
            var cudaConfig = baseConfig with { IsCudaProject = true, ManagedOnly = false };
            RunBatchProjectAndCollectEvents(cudaConfig, perKernelFailures);
        }
    }

    private static HashSet<string> CollectMatchingFailingCandidates(
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures,
        FuzzFailureSignature expectedSignature)
    {
        var failing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (kernelName, info) in perKernelFailures)
        {
            string mergedPaths = string.Join(",", info.Paths);
            if (expectedSignature.Matches(mergedPaths, info.FailureKind, info.Column))
            {
                failing.Add(kernelName);
            }
        }

        return failing;
    }

    private static void RunBatchProjectAndCollectEvents(
        BatchRunConfig config,
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures)
    {
        string hostOs = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unix";
        string projName = config.IsCudaProject ? "FuzzBatch.Cuda" : "FuzzBatch.Cpu";
        string csproj = Path.Combine(config.FullWorkDir, $"{projName}.csproj");
        string dll = Path.Combine(
            config.FullWorkDir, "bin", hostOs, projName, "Debug", "net10.0", $"{projName}.dll");

        if (!BuildBatchProject(config, csproj, hostOs, projName, perKernelFailures))
        {
            return;
        }

        if (TryExecuteBatchProcess(
            config, dll, perKernelFailures, out string stdout, out int runCode))
        {
            ParseFailureEvents(config, stdout, runCode, perKernelFailures);
        }
    }

    private static bool BuildBatchProject(
        BatchRunConfig config,
        string csproj,
        string hostOs,
        string projName,
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures)
    {
        string skipCpu = (!config.IsCudaProject && config.ManagedOnly)
            ? "-p:GSharpSkipCpuCompilation=true"
            : "-p:GSharpSkipCpuCompilation=false";
        string assetsPath = Path.Combine(config.FullWorkDir, "obj", hostOs, projName, "project.assets.json");
        string noRestore = File.Exists(assetsPath) ? "--no-restore" : string.Empty;
        int buildCode = RunDotnetProcess(
            $"build \"{csproj}\" -c Debug -m:1 -nodeReuse:false --no-dependencies {noRestore} {skipCpu}",
            out _,
            out _,
            out bool buildTimedOut,
            timeoutMs: 240_000);
        if (buildCode == 0)
        {
            return true;
        }

        if (!buildTimedOut &&
            config.Batch.Candidates.Count == 1 &&
            string.Equals(config.ExpectedSignature.FailureKind, "compile_error", StringComparison.OrdinalIgnoreCase))
        {
            string candName = config.Batch.Candidates[0].CandidateKernelName;
            string dialectPath = config.IsCudaProject
                ? "Cuda"
                : (config.ManagedOnly ? "Managed" : "Native");
            MergeCandidateFailure(
                perKernelFailures, candName, [dialectPath], "compile_error", config.ExpectedSignature.Column);
        }

        return false;
    }

    private static bool TryExecuteBatchProcess(
        BatchRunConfig config,
        string dll,
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures,
        out string stdout,
        out int runCode)
    {
        string runArgs = $"exec \"{dll}\"";
        if (config.ManagedOnly)
        {
            runArgs += " --managed-only";
        }

        if (config.Batch.InputSeed is { } s)
        {
            runArgs += string.Create(CultureInfo.InvariantCulture, $" --input-seed {s}");
        }

        bool runTimedOut;
        if (config.IsCudaProject && !config.NoGpuLock && !string.IsNullOrWhiteSpace(config.RepoRoot))
        {
            string lockScript = Path.Combine(Path.GetFullPath(config.RepoRoot), "scripts", "agent", "gpu-lock.js");
            string lockArgs = $"\"{lockScript}\" -- \"dotnet {runArgs}\"";
            runCode = RunProcess("node", lockArgs, out stdout, out _, out runTimedOut, timeoutMs: 120_000);
        }
        else
        {
            runCode = RunDotnetProcess(runArgs, out stdout, out _, out runTimedOut, timeoutMs: 120_000);
        }

        if (runTimedOut && config.Batch.Candidates.Count == 1)
        {
            string candName = config.Batch.Candidates[0].CandidateKernelName;
            string[] paths = config.IsCudaProject
                ? ["Cuda"]
                : (config.ManagedOnly ? ["Managed"] : ["Managed", "Native"]);
            MergeCandidateFailure(
                perKernelFailures, candName, paths, "hang", config.ExpectedSignature.Column);
            return false;
        }

        return !runTimedOut;
    }

    private static void ParseFailureEvents(
        BatchRunConfig config,
        string stdout,
        int runCode,
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures)
    {
        bool anyEvent = false;
        foreach (string line in FuzzSyntaxHelpers.SplitNonEmpty(stdout, "\r\n"))
        {
            if (TryParseEventLine(line, config.IsCudaProject, config.ManagedOnly, out var ev))
            {
                anyEvent = true;
                MergeCandidateFailure(perKernelFailures, ev.KernelName, ev.Paths, ev.FailureKind, ev.Column);
            }
        }

        if (!anyEvent && runCode != 0 && config.Batch.Candidates.Count == 1)
        {
            string candName = config.Batch.Candidates[0].CandidateKernelName;
            string[] paths = config.IsCudaProject
                ? ["Cuda"]
                : (config.ManagedOnly ? ["Managed"] : ["Native"]);
            MergeCandidateFailure(
                perKernelFailures, candName, paths, "crash", config.ExpectedSignature.Column);
        }
    }

    private static bool TryParseEventLine(
        string line,
        bool isCudaProject,
        bool managedOnly,
        out FuzzEventResult result)
    {
        result = default;
        ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed.ToString());
            return TryExtractEventFields(doc.RootElement, isCudaProject, managedOnly, out result);
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"Skipping a non-event output line: {ex.Message}");
            return false;
        }
    }

    private static bool TryExtractEventFields(
        JsonElement root,
        bool isCudaProject,
        bool managedOnly,
        out FuzzEventResult result)
    {
        result = default;
        if (!root.TryGetProperty("status", out var statusProp))
        {
            return false;
        }

        string? status = statusProp.GetString();
        if (status is not ("failed" or "harness_error"))
        {
            return false;
        }

        if (!root.TryGetProperty("kernel", out var kernelProp) || kernelProp.GetString() is not { } kName)
        {
            return false;
        }

        var paths = ExtractFailingPaths(root, isCudaProject, managedOnly);
        string failureKind = root.TryGetProperty("failureKind", out var kindProp) && kindProp.GetString() is { } fk
            ? fk
            : (status == "harness_error" ? "HarnessException" : "CellMismatch");
        string column = root.TryGetProperty("column", out var colProp) && colProp.GetString() is { } c
            ? c
            : "hash";

        result = new FuzzEventResult(kName, paths, failureKind, column);
        return true;
    }

    private static List<string> ExtractFailingPaths(JsonElement root, bool isCudaProject, bool managedOnly)
    {
        var paths = new List<string>();
        if (root.TryGetProperty("failingPaths", out var pathsProp) && pathsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in pathsProp.EnumerateArray())
            {
                if (elem.GetString() is { } p && !string.IsNullOrWhiteSpace(p))
                {
                    paths.Add(p);
                }
            }
        }

        if (paths.Count == 0)
        {
            paths.Add(isCudaProject ? "Cuda" : (managedOnly ? "Managed" : "Native"));
        }

        return paths;
    }

    private static void MergeCandidateFailure(
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures,
        string kernelName,
        IEnumerable<string> paths,
        string failureKind,
        string column)
    {
        if (!perKernelFailures.TryGetValue(kernelName, out var entry))
        {
            entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), failureKind, column);
        }

        foreach (string p in paths)
        {
            entry.Paths.Add(p);
        }

        perKernelFailures[kernelName] = entry;
    }

    private static ProcessStartInfo CreateUnixShellStartInfo(string command)
    {
        var psi = new ProcessStartInfo("/bin/sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        return psi;
    }

    private static int RunDotnetProcess(
        string arguments,
        out string stdout,
        out string stderr,
        out bool timedOut,
        int timeoutMs = 120_000) =>
        RunProcess("dotnet", arguments, out stdout, out stderr, out timedOut, timeoutMs);

    private static int RunProcess(
        string fileName,
        string arguments,
        out string stdout,
        out string stderr,
        out bool timedOut,
        int timeoutMs = 120_000) =>
        FuzzRunProcess.RunWithStartInfo(
            new ProcessStartInfo(fileName, arguments),
            out stdout,
            out stderr,
            out timedOut,
            timeoutMs);

    private static string? DetectHostCudaArchForReducer()
    {
        try
        {
            int code = RunProcess(
                "nvidia-smi",
                "--query-gpu=compute_cap --format=csv,noheader",
                out string stdout,
                out _,
                out bool timedOut,
                timeoutMs: 15_000);
            if (timedOut || code != 0)
            {
                return null;
            }

            var lines = FuzzSyntaxHelpers.SplitNonEmpty(stdout, "\r\n");
            if (lines.Count == 0)
            {
                return null;
            }

            ReadOnlySpan<char> firstLine = lines[0].AsSpan().Trim();
            if (firstLine.IsEmpty ||
                !Version.TryParse(firstLine, out var version) ||
                version.Build >= 0 ||
                version.Revision >= 0)
            {
                return null;
            }

            return string.Create(CultureInfo.InvariantCulture, $"compute_{version.Major}{version.Minor},sm_{version.Major}{version.Minor}");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
