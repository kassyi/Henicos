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
/// <param name="KernelFailures">The per-kernel failures, with the failing paths of both runs merged.</param>
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

    /// <summary>
    /// How long the CUDA run waits for the shared GPU lock before the CUDA stage of the batch is skipped. The run
    /// itself still has <see cref="RunTimeoutMs"/> once it holds the lock.
    /// </summary>
    internal const int GpuLockWaitMinutes = 10;

    /// <summary>
    /// Gets the arguments of a Debug <c>dotnet build</c>; <c>--no-restore</c> when <paramref name="restoredAssets"/>.
    /// </summary>
    internal static List<string> BuildArguments(string project, bool restoredAssets, params string[] extra)
    {
        var args = new List<string>
        {
            "build", project, "-c", FuzzBatchWorkspaceWriter.Configuration, "-m:1", "-nodeReuse:false",
        };
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
        var projects = Generate(seedHex);
        string kernelsSource = File.ReadAllText(Path.Combine(options.Paths.OutDir, "FuzzBatchKernels.cu.cs"));
        string casesSource = File.ReadAllText(Path.Combine(options.Paths.OutDir, "FuzzBatchCases.cs"));

        var stageFailures = new List<FuzzStageFailure>();
        var cpuEvents = RunStage(CpuStage(projects.Cpu), batchIndex, seedHex, stageFailures);
        var cudaEvents = options.Profile == FuzzProfile.All
            ? RunStage(CudaStage(projects.Cuda), batchIndex, seedHex, stageFailures)
            : [];

        var kernelFailures = FuzzFailureMerger.Merge(cpuEvents, cudaEvents);
        return new FuzzBatchResult(kernelsSource, casesSource, stageFailures, kernelFailures);
    }

    private FuzzBatchProjects Generate(string seedHex)
    {
        if (!FuzzRandom.TryParseSeed(seedHex, out ulong seed))
        {
            throw new FuzzRunAbortException($"Invalid batch seed {seedHex}.");
        }

        var generation = options.Generation;
        FuzzSwarmConfig.TryParseFocus(generation.SwarmFocus, out var swarmFocus);
        var fuzzOptions = new FuzzOptions
        {
            FuzzSeed = seed,
            InputSeed = generation.InputSeed,
            BatchSize = generation.BatchSize,
            Profile = options.Profile.ToCode(),
            AllowedIssues = new HashSet<int>(generation.AllowedIssues),
            SwarmFocus = swarmFocus,
            Metamorphic = generation.Metamorphic,
        };
        try
        {
            var batch = new ScalarFuzzGenerator(fuzzOptions).GenerateBatch();
            return FuzzBatchWorkspaceWriter.WriteBatch(batch, options.Paths.OutDir, options.Paths.RepoRoot, cudaArch);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            throw new FuzzRunAbortException($"fuzz-diff gen failed on seed {seedHex}: {ex.Message}");
        }
    }

    private BatchStage CpuStage(FuzzBatchProject project)
    {
        bool managedOnly = options.Profile == FuzzProfile.Managed;
        string firstPath = managedOnly ? "Managed" : "Native";
        var runArgs = new List<string> { "exec", project.OutputDll };
        if (managedOnly)
        {
            runArgs.Add("--managed-only");
        }

        AddInputSeed(runArgs);
        return new BatchStage(
            Name: "cpu",
            Dialect: "Cpu",
            Label: "CPU",
            Project: project,
            ExtraBuildArguments:
            [
                "--no-dependencies",
                $"-p:GSharpSkipCpuCompilation={(managedOnly ? "true" : "false")}",
            ],
            BuildFailurePaths: [firstPath],
            HangPaths: managedOnly ? ["Managed"] : ["Managed", "Native"],
            CrashPaths: [firstPath],
            Run: () => FuzzRunProcess.Run("dotnet", runArgs, options.Paths.RepoRoot, RunTimeoutMs));
    }

    private BatchStage CudaStage(FuzzBatchProject project) =>
        new(
            Name: "cuda",
            Dialect: "Cuda",
            Label: "CUDA",
            Project: project,
            ExtraBuildArguments: ["--no-dependencies"],
            BuildFailurePaths: ["Cuda"],
            HangPaths: ["Cuda"],
            CrashPaths: ["Cuda"],
            Run: () => RunCuda(project));

    // Builds the stage's project, runs it and records a build failure or hang, a run hang or a crash (a non-zero exit
    // with no events). Returns the events the run printed, or none when the build failed or the run was skipped.
    private List<JsonObject> RunStage(
        BatchStage stage,
        int batchIndex,
        string seedHex,
        List<FuzzStageFailure> stageFailures)
    {
        var project = stage.Project;
        var buildArgs = BuildArguments(project.ProjectPath, File.Exists(project.AssetsFile), stage.ExtraBuildArguments);
        var build = FuzzRunProcess.Run("dotnet", buildArgs, options.Paths.RepoRoot, BuildTimeoutMs);
        if (build.Status != 0)
        {
            string status = build.TimedOut ? "hang" : "compile_error";
            string name = build.TimedOut ? $"{stage.Name}_build_hang" : $"{stage.Name}_build";
            var details = StageDetails(stage, status, stage.BuildFailurePaths, seedHex, build);
            stageFailures.Add(new FuzzStageFailure(name, details));
            Console.Out.WriteLine(
                $"[fuzz-diff] Batch {batchIndex + 1} ({seedHex}): {stage.Label} build " +
                $"{(build.TimedOut ? "timed out" : "failed")}! Recorded to findings.");
            return [];
        }

        if (stage.Run() is not FuzzProcessResult run)
        {
            Console.Out.WriteLine(
                $"[fuzz-diff] Batch {batchIndex + 1} ({seedHex}): {stage.Label} stage skipped (GPU busy).");
            return [];
        }

        var events = FuzzRunProcess.ParseJsonLines(run.Stdout);
        if (run.TimedOut)
        {
            var timeout = new JsonObject { ["timeoutMs"] = RunTimeoutMs };
            var details = StageDetails(stage, "hang", stage.HangPaths, seedHex, run, timeout);
            stageFailures.Add(new FuzzStageFailure($"{stage.Name}_hang", details));
        }
        else if (run.Status != 0 && events.Count == 0)
        {
            var exitCode = new JsonObject { ["exitCode"] = run.Status };
            var details = StageDetails(stage, "crash", stage.CrashPaths, seedHex, run, exitCode);
            stageFailures.Add(new FuzzStageFailure($"{stage.Name}_crash", details));
        }

        return events;
    }

    // Runs the CUDA batch, under scripts/agent/gpu-lock.js unless the lock is skipped or already held. Returns null
    // when the lock wait timed out: the GPU was busy, which is no finding.
    private FuzzProcessResult? RunCuda(FuzzBatchProject project)
    {
        string repoRoot = options.Paths.RepoRoot;
        bool useLock = !options.NoGpuLock && !FuzzRunProcess.IsAlreadyInsideGpuLock(repoRoot);
        if (!useLock)
        {
            var runArgs = new List<string> { "exec", project.OutputDll };
            AddInputSeed(runArgs);
            return FuzzRunProcess.Run("dotnet", runArgs, repoRoot, RunTimeoutMs);
        }

        string lockScript = Path.Combine(repoRoot, "scripts", "agent", "gpu-lock.js");
        string inputSeedArg = options.Generation.InputSeed is int inputSeed
            ? $" --input-seed {inputSeed.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
        string[] lockArgs =
        [
            lockScript,
            "--timeout-minutes", GpuLockWaitMinutes.ToString(CultureInfo.InvariantCulture),
            "--", $"dotnet exec \"{project.OutputDll}\"{inputSeedArg}",
        ];
        var run = FuzzRunProcess.Run("node", lockArgs, repoRoot, (GpuLockWaitMinutes * 60_000) + RunTimeoutMs);
        return FuzzRunProcess.IsGpuLockWaitTimeout(run) ? null : run;
    }

    private void AddInputSeed(List<string> args)
    {
        if (options.Generation.InputSeed is int inputSeed)
        {
            args.Add("--input-seed");
            args.Add(inputSeed.ToString(CultureInfo.InvariantCulture));
        }
    }

    // status, failureKind, dialect, failingPaths, batchFuzzSeed, then the extra fields and the process output.
    private static JsonObject StageDetails(
        BatchStage stage,
        string status,
        string[] failingPaths,
        string seedHex,
        FuzzProcessResult result,
        JsonObject? extra = null)
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
            ["dialect"] = stage.Dialect,
            ["failingPaths"] = paths,
            ["batchFuzzSeed"] = seedHex,
        };
        foreach (var (name, value) in extra ?? [])
        {
            details[name] = value?.DeepClone();
        }

        details["stdout"] = result.Stdout;
        details["stderr"] = result.Stderr;
        return details;
    }

    /// <summary>What differs between the CPU and the CUDA stage of a batch.</summary>
    /// <param name="Name">The prefix of the stage failure names (<c>cpu</c>, <c>cuda</c>).</param>
    /// <param name="Dialect">The <c>dialect</c> of <c>finding.json</c>.</param>
    /// <param name="Label">The stage name in console messages.</param>
    /// <param name="Project">The batch project the stage builds and runs.</param>
    /// <param name="ExtraBuildArguments">The <c>dotnet build</c> arguments after the common ones.</param>
    /// <param name="BuildFailurePaths">The failing paths of a build failure or build hang.</param>
    /// <param name="HangPaths">The failing paths of a run that timed out.</param>
    /// <param name="CrashPaths">The failing paths of a run that exited with an error and printed no event.</param>
    /// <param name="Run">Runs the built batch; <see langword="null"/> when the run was skipped.</param>
    private sealed record BatchStage(
        string Name,
        string Dialect,
        string Label,
        FuzzBatchProject Project,
        string[] ExtraBuildArguments,
        string[] BuildFailurePaths,
        string[] HangPaths,
        string[] CrashPaths,
        Func<FuzzProcessResult?> Run);
}

/// <summary>Merges the failures that the CPU and the CUDA run report for the same kernel into one failure.</summary>
internal sealed class FuzzFailureMerger
{
    private readonly List<FuzzStageFailure> _batchFailures = [];
    private readonly List<string> _kernelOrder = [];
    private readonly Dictionary<string, (string Stage, JsonObject Event)> _byKernel = new(StringComparer.Ordinal);

    /// <summary>Merges per kernel; a failure without a kernel name is kept as a batch failure of its stage.</summary>
    internal static List<FuzzStageFailure> Merge(
        IReadOnlyList<JsonObject> cpuEvents,
        IReadOnlyList<JsonObject> cudaEvents)
    {
        var merger = new FuzzFailureMerger();
        merger.Ingest(cpuEvents, "cpu");
        merger.Ingest(cudaEvents, "cuda");
        return merger.Result();
    }

    private List<FuzzStageFailure> Result()
    {
        var failures = new List<FuzzStageFailure>(_batchFailures);
        foreach (string kernel in _kernelOrder)
        {
            var (stage, ev) = _byKernel[kernel];
            failures.Add(new FuzzStageFailure(stage, ev));
        }

        return failures;
    }

    private void Ingest(IReadOnlyList<JsonObject> events, string stagePrefix)
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
                _batchFailures.Add(new FuzzStageFailure($"{stagePrefix}_batch", ev));
                continue;
            }

            if (!_byKernel.TryGetValue(kernel, out var existing))
            {
                var copy = (JsonObject)ev.DeepClone();
                copy["failingPaths"] = ev["failingPaths"] is JsonArray paths
                    ? (JsonArray)paths.DeepClone()
                    : new JsonArray();
                _byKernel[kernel] = ($"{stagePrefix}_{kernel}", copy);
                _kernelOrder.Add(kernel);
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
            _byKernel[kernel] = (stagePrefix == "cuda" ? $"cuda_{kernel}" : existing.Stage, existing.Event);
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
            if (item is JsonValue value &&
                value.TryGetValue(out string? text) &&
                text is not null &&
                !into.Contains(text))
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
