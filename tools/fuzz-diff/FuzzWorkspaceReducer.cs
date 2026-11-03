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
        string current = initialSource;
        int iterations = 0;

        string hoisted = FuzzReducer.HoistLocalsToDefault(initialSource);
        if (!string.Equals(hoisted, initialSource, StringComparison.Ordinal))
        {
            iterations++;
            if (EvaluateSingleCandidateInWorkspace(hoisted, fuzzSeed, inputSeed, profile, workDir, repoRoot, expectedSignature, noGpuLock))
            {
                current = hoisted;
            }
        }

        IReadOnlyList<IReadOnlyList<int>>? priorSuccessRemovals = null;

        for (int round = 0; round < 14; round++)
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
                useCuda: profile == "G",
                noGpuLock,
                expectedSignature,
                managedOnly: profile == "M");

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

        string allPruned = FuzzReducer.PruneUnusedHoistedLocals(current, static _ => true);
        if (!string.Equals(allPruned, current, StringComparison.Ordinal))
        {
            iterations++;
            if (EvaluateSingleCandidateInWorkspace(allPruned, fuzzSeed, inputSeed, profile, workDir, repoRoot, expectedSignature, noGpuLock))
            {
                current = allPruned;
            }
            else
            {
                current = FuzzReducer.PruneUnusedHoistedLocals(
                    current,
                    cand =>
                    {
                        iterations++;
                        return EvaluateSingleCandidateInWorkspace(cand, fuzzSeed, inputSeed, profile, workDir, repoRoot, expectedSignature, noGpuLock);
                    });
            }
        }

        return (current, iterations);
    }

    internal static bool RunExternalShellPredicate(string command)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/d /s /c \"{command}\"")
            : CreateUnixShellStartInfo(command);
        int code = RunProcessWithStartInfo(psi, out _, out _, out bool timedOut, timeoutMs: 120_000);
        return !timedOut && code == 0;
    }

    private static string ReplaceKernelMethodInUnit(string unitSource, string newMethodSource)
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

        var kernel = kernels[0];
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
        };
        var singleBatch = specBatch with
        {
            KernelsSource = FuzzSyntaxHelpers.RenderFullNode(root.ReplaceNode(kernel, exactMethod)),
            Candidates = [exactCand],
        };

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

    private static HashSet<string> EvaluateSpeculativeBatchInWorkspace(
        SpeculativeCandidateBatch batch,
        string workDir,
        string? repoRoot,
        bool useCuda,
        bool noGpuLock,
        FuzzFailureSignature expectedSignature,
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

        bool needsCpuPass = !useCuda ||
                            expectedSignature.FailingPathsMask.Contains("Managed", StringComparison.OrdinalIgnoreCase) ||
                            expectedSignature.FailingPathsMask.Contains("Native", StringComparison.OrdinalIgnoreCase);

        var perKernelFailures = new Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)>(StringComparer.Ordinal);

        if (needsCpuPass)
        {
            RunBatchProjectAndCollectEvents(
                fullWorkDir,
                repoRoot,
                batch,
                isCudaProject: false,
                noGpuLock: true,
                managedOnly: managedOnly,
                expectedSignature,
                perKernelFailures);
        }

        if (useCuda)
        {
            RunBatchProjectAndCollectEvents(
                fullWorkDir,
                repoRoot,
                batch,
                isCudaProject: true,
                noGpuLock: noGpuLock,
                managedOnly: false,
                expectedSignature,
                perKernelFailures);
        }

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
        string fullWorkDir,
        string? repoRoot,
        SpeculativeCandidateBatch batch,
        bool isCudaProject,
        bool noGpuLock,
        bool managedOnly,
        FuzzFailureSignature expectedSignature,
        Dictionary<string, (HashSet<string> Paths, string FailureKind, string Column)> perKernelFailures)
    {
        string hostOs = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unix";
        string projName = isCudaProject ? "FuzzBatch.Cuda" : "FuzzBatch.Cpu";
        string csproj = Path.Combine(fullWorkDir, $"{projName}.csproj");
        string dll = Path.Combine(fullWorkDir, "bin", hostOs, projName, "Debug", "net10.0", $"{projName}.dll");

        string skipCpu = (!isCudaProject && managedOnly) ? "-p:GSharpSkipCpuCompilation=true" : "-p:GSharpSkipCpuCompilation=false";
        string assetsPath = Path.Combine(fullWorkDir, "obj", hostOs, projName, "project.assets.json");
        string noRestore = File.Exists(assetsPath) ? "--no-restore" : string.Empty;
        int buildCode = RunDotnetProcess(
            $"build \"{csproj}\" -c Debug -m:1 -nodeReuse:false --no-dependencies {noRestore} {skipCpu}",
            out _,
            out _,
            out bool buildTimedOut,
            timeoutMs: 240_000);
        if (buildCode != 0)
        {
            if (!buildTimedOut &&
                batch.Candidates.Count == 1 &&
                string.Equals(expectedSignature.FailureKind, "compile_error", StringComparison.OrdinalIgnoreCase))
            {
                string candName = batch.Candidates[0].CandidateKernelName;
                string dialectPath = isCudaProject ? "Cuda" : (managedOnly ? "Managed" : "Native");
                MergeCandidateFailure(perKernelFailures, candName, [dialectPath], "compile_error", expectedSignature.Column);
            }

            return;
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
        bool runTimedOut;
        int runCode;
        if (isCudaProject && !noGpuLock && !string.IsNullOrWhiteSpace(repoRoot))
        {
            string lockScript = Path.Combine(Path.GetFullPath(repoRoot), "scripts", "agent", "gpu-lock.js");
            runCode = RunProcess("node", $"\"{lockScript}\" -- \"dotnet {runArgs}\"", out stdout, out _, out runTimedOut, timeoutMs: 120_000);
        }
        else
        {
            runCode = RunDotnetProcess(runArgs, out stdout, out _, out runTimedOut, timeoutMs: 120_000);
        }

        if (runTimedOut && batch.Candidates.Count == 1)
        {
            string candName = batch.Candidates[0].CandidateKernelName;
            string[] paths = isCudaProject ? ["Cuda"] : (managedOnly ? ["Managed"] : ["Managed", "Native"]);
            MergeCandidateFailure(perKernelFailures, candName, paths, "hang", expectedSignature.Column);
            return;
        }

        bool anyEvent = false;
        foreach (string line in FuzzSyntaxHelpers.SplitNonEmpty(stdout, "\r\n"))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed.ToString());
                var root = doc.RootElement;
                if (!root.TryGetProperty("status", out var statusProp))
                {
                    continue;
                }

                string? status = statusProp.GetString();
                if (status is not ("failed" or "harness_error"))
                {
                    continue;
                }

                if (!root.TryGetProperty("kernel", out var kernelProp) || kernelProp.GetString() is not { } kName)
                {
                    continue;
                }

                anyEvent = true;
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

                string fKind = root.TryGetProperty("failureKind", out var kindProp) && kindProp.GetString() is { } fk
                    ? fk
                    : (status == "harness_error" ? "HarnessException" : "CellMismatch");
                string col = root.TryGetProperty("column", out var colProp) && colProp.GetString() is { } c
                    ? c
                    : "hash";

                MergeCandidateFailure(perKernelFailures, kName, paths, fKind, col);
            }
            catch (JsonException ex)
            {
                // [WHY] the runner also prints lines that only look like JSON; they carry no candidate result
                Trace.WriteLine($"Skipping a non-event output line: {ex.Message}");
            }
        }

        if (!anyEvent && runCode != 0 && batch.Candidates.Count == 1)
        {
            string candName = batch.Candidates[0].CandidateKernelName;
            string[] paths = isCudaProject ? ["Cuda"] : (managedOnly ? ["Managed"] : ["Native"]);
            MergeCandidateFailure(perKernelFailures, candName, paths, "crash", expectedSignature.Column);
        }
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
        RunProcessWithStartInfo(new ProcessStartInfo(fileName, arguments), out stdout, out stderr, out timedOut, timeoutMs);

    private static int RunProcessWithStartInfo(
        ProcessStartInfo psi,
        out string stdout,
        out string stderr,
        out bool timedOut,
        int timeoutMs = 120_000)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {psi.FileName}.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(timeoutMs))
        {
            timedOut = true;
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                // [WHY] the process may exit between the timeout and Kill; the caller already treats it as timed out
                Trace.WriteLine($"Could not kill {psi.FileName} after the timeout: {ex.Message}");
            }

            proc.WaitForExit(5_000);
            stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            return 124;
        }

        proc.WaitForExit();
        timedOut = false;
        stdout = stdoutTask.GetAwaiter().GetResult();
        stderr = stderrTask.GetAwaiter().GetResult();
        return proc.ExitCode;
    }

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
