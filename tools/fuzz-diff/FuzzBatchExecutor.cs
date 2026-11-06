using System.Globalization;
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>A failure of one stage of a batch (or of one kernel), named by the stage that found it.</summary>
/// <param name="Stage">The stage name, which names the finding directory.</param>
/// <param name="Details">The <c>finding.json</c> content.</param>
internal readonly record struct FuzzStageFailure(string Stage, JsonObject Details);

/// <summary>The outcome of one batch: its generated sources and every failure found.</summary>
/// <param name="KernelsSource">The text of <c>FuzzBatchKernels.cu.cs</c>.</param>
/// <param name="CasesSource">The text of <c>FuzzBatchCases.cs</c>.</param>
/// <param name="StageFailures">The builds and runs that failed as a whole (compile error, hang, crash).</param>
/// <param name="KernelFailures">The per-kernel failures, with the failing paths of the CPU and CUDA runs merged.</param>
internal sealed record FuzzBatchResult(
    string KernelsSource,
    string CasesSource,
    IReadOnlyList<FuzzStageFailure> StageFailures,
    IReadOnlyList<FuzzStageFailure> KernelFailures);

/// <summary>
/// Generates one batch of kernels, builds the CPU and CUDA batch projects and runs them, and turns what they print
/// into failures. It records nothing; <see cref="FuzzFindingRecorder"/> does.
/// </summary>
internal sealed class FuzzBatchExecutor(RunCommandOptions options, string? cudaArch)
{
    internal const int BuildTimeoutMs = 240_000;
    internal const int RunTimeoutMs = 120_000;

    /// <summary>Gets the arguments of a Debug <c>dotnet build</c> that uses the restored assets when <paramref name="restoredAssets"/> is set.</summary>
    internal static List<string> BuildArguments(string project, bool restoredAssets, params string[] extra)
    {
        var args = new List<string> { "build", project, "-c", "Debug", "-m:1", "-nodeReuse:false" };
        args.AddRange(extra);
        if (restoredAssets)
        {
            args.Add("--no-restore");
        }

        return args;
    }

    /// <summary>Generates, builds and runs the batch with the fuzz seed <paramref name="seedHex"/>.</summary>
    internal FuzzBatchResult RunSingleBatch(int batchIndex, string seedHex)
    {
        Generate(seedHex);
        string kernelsSource = File.ReadAllText(Path.Combine(options.OutDir, "FuzzBatchKernels.cu.cs"));
        string casesSource = File.ReadAllText(Path.Combine(options.OutDir, "FuzzBatchCases.cs"));

        var stageFailures = new List<FuzzStageFailure>();
        var cpuEvents = RunCpu(batchIndex, seedHex, stageFailures);
        var cudaEvents = options.Profile == "G" ? RunCuda(batchIndex, seedHex, stageFailures) : [];

        return new FuzzBatchResult(kernelsSource, casesSource, stageFailures, FuzzFailureMerger.Merge(cpuEvents, cudaEvents));
    }

    private void Generate(string seedHex)
    {
        if (!FuzzRandom.TryParseSeed(seedHex, out ulong seed))
        {
            throw new FuzzRunAbortException($"Invalid batch seed {seedHex}.");
        }

        FuzzSwarmConfig.TryParseFocus(options.SwarmFocus, out var swarmFocus);
        var fuzzOptions = new FuzzOptions
        {
            FuzzSeed = seed,
            InputSeed = options.InputSeed,
            BatchSize = options.BatchSize,
            Profile = options.Profile,
            AllowedIssues = new HashSet<int>(options.AllowedIssues),
            SwarmFocus = swarmFocus,
            Metamorphic = options.Metamorphic,
        };
        try
        {
            var batch = new ScalarFuzzGenerator(fuzzOptions).GenerateBatch();
            FuzzBatchWorkspaceWriter.WriteBatch(batch, options.OutDir, options.RepoRoot, cudaArch);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            throw new FuzzRunAbortException($"fuzz-diff gen failed on seed {seedHex}: {ex.Message}");
        }
    }

    private List<JsonObject> RunCpu(int batchIndex, string seedHex, List<FuzzStageFailure> stageFailures)
    {
        string project = Path.Combine(options.WorkDir, "FuzzBatch.Cpu.csproj");
        string dll = Path.Combine(options.WorkDir, "bin", FuzzHostOs.Name, "FuzzBatch.Cpu", "Debug", "net10.0", "FuzzBatch.Cpu.dll");
        string assets = Path.Combine(options.WorkDir, "obj", FuzzHostOs.Name, "FuzzBatch.Cpu", "project.assets.json");
        string skipCpuCpp = options.Profile == "M" ? "true" : "false";
        var buildArgs = BuildArguments(project, File.Exists(assets), "--no-dependencies", $"-p:GSharpSkipCpuCompilation={skipCpuCpp}");

        var build = FuzzRunProcess.Run("dotnet", buildArgs, options.RepoRoot, BuildTimeoutMs);
        if (build.Status != 0)
        {
            string status = build.TimedOut ? "hang" : "compile_error";
            stageFailures.Add(new FuzzStageFailure(
                build.TimedOut ? "cpu_build_hang" : "cpu_build",
                StageDetails(status, "Cpu", [options.Profile == "M" ? "Managed" : "Native"], seedHex, build)));
            Console.Out.WriteLine($"[fuzz-diff] Batch {batchIndex + 1} ({seedHex}): CPU build {(build.TimedOut ? "timed out" : "failed")}! Recorded to findings.");
            return [];
        }

        var runArgs = new List<string> { "exec", dll };
        if (options.Profile == "M")
        {
            runArgs.Add("--managed-only");
        }

        AddInputSeed(runArgs);

        var run = FuzzRunProcess.Run("dotnet", runArgs, options.RepoRoot, RunTimeoutMs);
        var events = FuzzRunProcess.ParseJsonLines(run.Stdout);
        if (run.TimedOut)
        {
            stageFailures.Add(new FuzzStageFailure(
                "cpu_hang",
                StageDetails("hang", "Cpu", options.Profile == "M" ? ["Managed"] : ["Managed", "Native"], seedHex, run, "timeoutMs", RunTimeoutMs)));
        }
        else if (run.Status != 0 && events.Count == 0)
        {
            stageFailures.Add(new FuzzStageFailure(
                "cpu_crash",
                StageDetails("crash", "Cpu", [options.Profile == "M" ? "Managed" : "Native"], seedHex, run, "exitCode", run.Status)));
        }

        return events;
    }

    private List<JsonObject> RunCuda(int batchIndex, string seedHex, List<FuzzStageFailure> stageFailures)
    {
        string project = Path.Combine(options.WorkDir, "FuzzBatch.Cuda.csproj");
        string dll = Path.Combine(options.WorkDir, "bin", FuzzHostOs.Name, "FuzzBatch.Cuda", "Debug", "net10.0", "FuzzBatch.Cuda.dll");
        string assets = Path.Combine(options.WorkDir, "obj", FuzzHostOs.Name, "FuzzBatch.Cuda", "project.assets.json");
        var buildArgs = BuildArguments(project, File.Exists(assets), "--no-dependencies");

        var build = FuzzRunProcess.Run("dotnet", buildArgs, options.RepoRoot, BuildTimeoutMs);
        if (build.Status != 0)
        {
            string status = build.TimedOut ? "hang" : "compile_error";
            stageFailures.Add(new FuzzStageFailure(
                build.TimedOut ? "cuda_build_hang" : "cuda_build",
                StageDetails(status, "Cuda", ["Cuda"], seedHex, build)));
            Console.Out.WriteLine($"[fuzz-diff] Batch {batchIndex + 1} ({seedHex}): CUDA build {(build.TimedOut ? "timed out" : "failed")}! Recorded to findings.");
            return [];
        }

        bool useLock = !options.NoGpuLock && !FuzzRunProcess.IsAlreadyInsideGpuLock(options.RepoRoot);
        FuzzProcessResult run;
        if (useLock)
        {
            string lockScript = Path.Combine(options.RepoRoot, "scripts", "agent", "gpu-lock.js");
            string inputSeedArg = options.InputSeed is int inputSeed ? $" --input-seed {inputSeed.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
            run = FuzzRunProcess.Run("node", [lockScript, "--", $"dotnet exec \"{dll}\"{inputSeedArg}"], options.RepoRoot, RunTimeoutMs);
        }
        else
        {
            var runArgs = new List<string> { "exec", dll };
            AddInputSeed(runArgs);
            run = FuzzRunProcess.Run("dotnet", runArgs, options.RepoRoot, RunTimeoutMs);
        }

        var events = FuzzRunProcess.ParseJsonLines(run.Stdout);
        if (run.TimedOut)
        {
            stageFailures.Add(new FuzzStageFailure("cuda_hang", StageDetails("hang", "Cuda", ["Cuda"], seedHex, run, "timeoutMs", RunTimeoutMs)));
        }
        else if (run.Status != 0 && events.Count == 0)
        {
            stageFailures.Add(new FuzzStageFailure("cuda_crash", StageDetails("crash", "Cuda", ["Cuda"], seedHex, run, "exitCode", run.Status)));
        }

        return events;
    }

    private void AddInputSeed(List<string> args)
    {
        if (options.InputSeed is int inputSeed)
        {
            args.Add("--input-seed");
            args.Add(inputSeed.ToString(CultureInfo.InvariantCulture));
        }
    }

    // status, failureKind, dialect, failingPaths, batchFuzzSeed, then the extra fields and the process output.
    private static JsonObject StageDetails(string status, string dialect, string[] failingPaths, string seedHex, FuzzProcessResult result, string? extraName = null, int extraValue = 0)
    {
        var paths = new JsonArray();
        foreach (string path in failingPaths)
        {
            paths.Add(path);
        }

        var details = new JsonObject
        {
            ["status"] = status,
            ["failureKind"] = status,
            ["dialect"] = dialect,
            ["failingPaths"] = paths,
            ["batchFuzzSeed"] = seedHex,
        };
        if (extraName is not null)
        {
            details[extraName] = extraValue;
        }

        details["stdout"] = result.Stdout;
        details["stderr"] = result.Stderr;
        return details;
    }
}

/// <summary>Merges the failures that the CPU run and the CUDA run report for the same kernel into one failure.</summary>
internal static class FuzzFailureMerger
{
    /// <summary>Merges per kernel; a failure without a kernel name is kept as a batch failure of its stage.</summary>
    internal static List<FuzzStageFailure> Merge(IReadOnlyList<JsonObject> cpuEvents, IReadOnlyList<JsonObject> cudaEvents)
    {
        var batchFailures = new List<FuzzStageFailure>();
        var kernelOrder = new List<string>();
        var byKernel = new Dictionary<string, (string Stage, JsonObject Event)>(StringComparer.Ordinal);

        Ingest(cpuEvents, "cpu", batchFailures, kernelOrder, byKernel);
        Ingest(cudaEvents, "cuda", batchFailures, kernelOrder, byKernel);

        foreach (string kernel in kernelOrder)
        {
            var (stage, ev) = byKernel[kernel];
            batchFailures.Add(new FuzzStageFailure(stage, ev));
        }

        return batchFailures;
    }

    private static void Ingest(
        IReadOnlyList<JsonObject> events,
        string stagePrefix,
        List<FuzzStageFailure> batchFailures,
        List<string> kernelOrder,
        Dictionary<string, (string Stage, JsonObject Event)> byKernel)
    {
        foreach (var ev in events)
        {
            string? status = FuzzRunProcess.GetString(ev, "status");
            if (status is not ("failed" or "harness_error"))
            {
                continue;
            }

            string? kernel = FuzzRunProcess.GetString(ev, "kernel");
            if (string.IsNullOrEmpty(kernel))
            {
                batchFailures.Add(new FuzzStageFailure($"{stagePrefix}_batch", ev));
                continue;
            }

            if (!byKernel.TryGetValue(kernel, out var existing))
            {
                var copy = (JsonObject)ev.DeepClone();
                copy["failingPaths"] = ev["failingPaths"] is JsonArray paths ? (JsonArray)paths.DeepClone() : new JsonArray();
                byKernel[kernel] = ($"{stagePrefix}_{kernel}", copy);
                kernelOrder.Add(kernel);
                continue;
            }

            var merged = new List<string>();
            AddPaths(existing.Event["failingPaths"], merged);
            AddPaths(ev["failingPaths"], merged);
            SortByPathOrder(merged);
            var sorted = new JsonArray();
            foreach (string path in merged)
            {
                sorted.Add(path);
            }

            existing.Event["failingPaths"] = sorted;
            byKernel[kernel] = (stagePrefix == "cuda" ? $"cuda_{kernel}" : existing.Stage, existing.Event);
        }
    }

    private static void AddPaths(JsonNode? node, List<string> into)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue(out string? text) && text is not null && !into.Contains(text))
            {
                into.Add(text);
            }
        }
    }

    // Insertion sort: stable, so paths of the same rank keep the order in which they were reported.
    private static void SortByPathOrder(List<string> paths)
    {
        for (int i = 1; i < paths.Count; i++)
        {
            string current = paths[i];
            int j = i - 1;
            while (j >= 0 && PathRank(paths[j]) > PathRank(current))
            {
                paths[j + 1] = paths[j];
                j--;
            }

            paths[j + 1] = current;
        }
    }

    private static int PathRank(string path) =>
        path.ToLowerInvariant() switch
        {
            "managed" => 0,
            "native" => 1,
            "cuda" => 2,
            _ => 9,
        };
}
