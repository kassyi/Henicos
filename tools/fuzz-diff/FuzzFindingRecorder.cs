using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Records the failures of a batch under <c>&lt;work-dir&gt;/findings</c>: classifies a failure against the known
/// issues, reduces and promotes it through <c>fuzz-diff reduce</c> and <c>fuzz-diff promote</c> (once per kernel),
/// and writes <c>finding.json</c>, the batch sources and one line of <c>occurrences.jsonl</c>.
/// </summary>
internal sealed class FuzzFindingRecorder(
    RunCommandOptions options,
    string findingsDir,
    IReadOnlyList<KnownIssueEntry> catalog)
{
    // A reduction runs up to 16 rounds, each with its own batch build and run; the reducer times out each process
    // itself, so this only stops a reducer that hangs between them.
    private const int ReduceTimeoutMs = 30 * 60_000;
    private const int PromoteTimeoutMs = 60_000;

    private readonly HashSet<string> _reducedKernels = new(StringComparer.Ordinal);

    /// <summary>Records every failure of <paramref name="batch"/> and adds it to <paramref name="totals"/>.</summary>
    internal void Handle(string seedHex, FuzzBatchResult batch, FuzzRunTotals totals)
    {
        // A stage failure is counted before it is recorded, a kernel failure after its classification.
        foreach (var failure in batch.StageFailures)
        {
            totals.Count(failure.Details);
            Record(seedHex, failure, batch);
        }

        foreach (var failure in batch.KernelFailures)
        {
            totals.ReductionMs += Record(seedHex, failure, batch);
            totals.Count(failure.Details);
        }
    }

    /// <summary>
    /// Marks the failure with the first known issue of <paramref name="catalog"/> whose classify-only paths are
    /// exactly the failing paths (ignoring case). Compile errors and hangs are never classified.
    /// </summary>
    internal static void Classify(JsonObject details, IReadOnlyList<KnownIssueEntry> catalog)
    {
        string? status = FuzzRunProcess.GetString(details, "status");
        if (status is "compile_error" or "hang")
        {
            return;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (details["failingPaths"] is JsonArray array)
        {
            foreach (var item in array)
            {
                paths.Add(item?.ToString() ?? string.Empty);
            }
        }

        foreach (var entry in catalog)
        {
            if (entry.ClassifyOnlyPaths is { Count: > 0 } expected && paths.SetEquals(expected))
            {
                details["suspectedIssue"] = entry.Issue;
                details["suspectedIssueNote"] =
                    $"#{entry.Issue} suspected ({string.Join(",", expected)} only; {entry.Summary})";
                return;
            }
        }
    }

    private long Record(string seedHex, FuzzStageFailure failure, FuzzBatchResult batch)
    {
        JsonObject details = failure.Details;
        Directory.CreateDirectory(findingsDir);
        Classify(details, catalog);

        string findingDir = Path.Combine(findingsDir, $"{seedHex}_{failure.Stage}");
        long reductionMs = 0;

        string? kernel = FuzzRunProcess.GetString(details, "kernel");
        if (kernel is not null && ShouldReduce(seedHex, kernel, details))
        {
            _reducedKernels.Add($"{seedHex}:{kernel}");
            var stopwatch = Stopwatch.StartNew();
            findingDir = ReduceAndPromote(seedHex, failure.Stage, kernel, details, batch.KernelsSource) ?? findingDir;
            reductionMs = stopwatch.ElapsedMilliseconds;
        }

        Directory.CreateDirectory(findingDir);
        File.WriteAllText(Path.Combine(findingDir, "FuzzBatchKernels.cu.cs"), batch.KernelsSource, FuzzJson.Utf8NoBom);
        File.WriteAllText(Path.Combine(findingDir, "FuzzBatchCases.cs"), batch.CasesSource, FuzzJson.Utf8NoBom);
        FuzzJson.Write(Path.Combine(findingDir, "finding.json"), details);
        FuzzJson.AppendLine(Path.Combine(findingsDir, "occurrences.jsonl"), details);
        return reductionMs;
    }

    // Reduce unless reduction is off, the failure is a hang, it is classified to a known issue that --allow-issue did
    // not re-enable, or this kernel of this batch was reduced already.
    private bool ShouldReduce(string seedHex, string kernel, JsonObject details)
    {
        bool knownClassified = details["suspectedIssue"] is JsonNode suspected &&
            !options.Generation.IsAllowed(suspected.GetValue<int>());
        return !options.NoReduce &&
            kernel.Length > 0 &&
            FuzzRunProcess.GetString(details, "status") != "hang" &&
            !knownClassified &&
            !_reducedKernels.Contains($"{seedHex}:{kernel}");
    }

    // Runs `fuzz-diff reduce` on the kernel and, when that succeeds, `fuzz-diff promote` on the reduced source.
    // Returns the promoted repro's directory, or null when there is none.
    private string? ReduceAndPromote(string seedHex, string stage, string kernel, JsonObject details, string source)
    {
        if (ReduceRequest.From(details, stage) is not ReduceRequest request)
        {
            return null;
        }

        string sourcePath = Path.Combine(findingsDir, $"_tmp_{seedHex}_{kernel}.cu.cs");
        string reducedPath = Path.Combine(findingsDir, $"_tmp_{seedHex}_{kernel}.reduced.cu.cs");
        try
        {
            File.WriteAllText(sourcePath, source, FuzzJson.Utf8NoBom);
            var target = new ReduceTarget(seedHex, kernel, sourcePath, reducedPath);
            if (RunReduce(request, target) is not JsonObject reduced)
            {
                return null;
            }

            ApplyReduced(reduced, details);
            return RunPromote(request, target, details);
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(reducedPath);
        }
    }

    // Returns the "reduced" event of the reducer (empty when it printed none), or null when the reduction failed.
    private JsonObject? RunReduce(ReduceRequest request, ReduceTarget target)
    {
        var reduceOptions = new ReduceCommandOptions(
            SourcePath: target.SourcePath,
            FuzzSeed: target.FuzzSeed,
            KernelName: target.Kernel,
            InputSeed: request.InputSeed,
            Profile: request.Profile,
            WorkDir: Path.Combine(options.Paths.WorkDir, "_reduce"),
            RepoRoot: options.Paths.RepoRoot,
            TestCmd: null,
            OutPath: target.ReducedPath,
            FailingPaths: request.FailingPaths,
            FailureKind: request.FailureKind,
            Column: request.Column,
            NoGpuLock: options.NoGpuLock || FuzzRunProcess.IsAlreadyInsideGpuLock(options.Paths.RepoRoot));
        var reduce = RunSelf("reduce", ReduceCommand.ToArguments(reduceOptions), ReduceTimeoutMs);
        if (reduce.Status != 0 || !File.Exists(target.ReducedPath))
        {
            return null;
        }

        return FindEvent(FuzzRunProcess.ParseJsonLines(reduce.Stdout), "reduced") ?? [];
    }

    private static void ApplyReduced(JsonObject reduced, JsonObject details)
    {
        details["fingerprint"] = reduced["fingerprint"]?.DeepClone();
        CopyIfPresent(reduced, "originalStatements", details);
        CopyIfPresent(reduced, "reducedStatements", details);
    }

    // Promotes the reduced kernel and returns the directory of its repro, or null when promote wrote none.
    private string? RunPromote(ReduceRequest request, ReduceTarget target, JsonObject details)
    {
        var promoteOptions = new PromoteCommandOptions(
            SourcePath: target.ReducedPath,
            FuzzSeed: target.FuzzSeed,
            InputSeed: request.InputSeed,
            FailingPaths: request.FailingPaths,
            FailureKind: request.FailureKind,
            Column: request.Column,
            FindingsDir: findingsDir,
            KernelName: target.Kernel);
        var promote = RunSelf("promote", PromoteCommand.ToArguments(promoteOptions), PromoteTimeoutMs);
        JsonObject? promoted = FindEvent(FuzzRunProcess.ParseJsonLines(promote.Stdout), "promoted");
        string? reproPath = promoted is null ? null : FuzzRunProcess.GetString(promoted, "reproKernelPath");
        if (promoted is null || string.IsNullOrEmpty(reproPath))
        {
            return null;
        }

        if (promoted["fingerprint"] is JsonNode fingerprint)
        {
            details["fingerprint"] = fingerprint.DeepClone();
        }

        details["reproKernelPath"] = reproPath;
        details["knownDivergenceEntry"] = promoted["knownDivergenceEntry"]?.DeepClone();
        return Path.GetDirectoryName(reproPath);
    }

    // Reduce and promote run as child processes of this tool: a reduction can hang and is stopped at its time limit.
    private FuzzProcessResult RunSelf(string command, List<string> arguments, int timeoutMs)
    {
        var args = new List<string> { "exec", typeof(Program).Assembly.Location, command };
        args.AddRange(arguments);
        return FuzzRunProcess.Run("dotnet", args, options.Paths.RepoRoot, timeoutMs);
    }

    private static JsonObject? FindEvent(List<JsonObject> events, string status)
    {
        foreach (var ev in events)
        {
            if (FuzzRunProcess.GetString(ev, "status") == status)
            {
                return ev;
            }
        }

        return null;
    }

    private static void CopyIfPresent(JsonObject from, string name, JsonObject into)
    {
        if (from[name] is JsonNode value)
        {
            into[name] = value.DeepClone();
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>The kernel to reduce and the temporary files of its reduction.</summary>
    private sealed record ReduceTarget(string SeedHex, string Kernel, string SourcePath, string ReducedPath)
    {
        // The batch seed is written by FuzzRandom.FormatSeed, so it always parses.
        internal ulong FuzzSeed => FuzzRandom.TryParseSeed(SeedHex, out ulong seed) ? seed : 0UL;
    }
}

/// <summary>
/// What <c>fuzz-diff reduce</c> and <c>fuzz-diff promote</c> are told about a failure, read from its
/// <c>finding.json</c> details, with the fallbacks of a failure that lacks a field.
/// </summary>
/// <param name="FailingPaths">The failing paths, comma-separated.</param>
/// <param name="FailureKind">The failure kind.</param>
/// <param name="Column">The output column that differs.</param>
/// <param name="InputSeed">The input seed that shows the failure.</param>
/// <param name="Profile">The reduction profile: <c>G</c> when the failure involves CUDA, else <c>C</c>.</param>
internal sealed record ReduceRequest(
    string FailingPaths,
    string FailureKind,
    string Column,
    int InputSeed,
    string Profile)
{
    /// <summary>The failing paths of a failure that names neither paths nor a dialect.</summary>
    internal const string DefaultFailingPaths = "Native,Cuda";

    /// <summary>The failure kind of a failure that names neither a kind nor a status.</summary>
    internal const string DefaultFailureKind = "CellMismatch";

    /// <summary>The column of a failure that names none.</summary>
    internal const string DefaultColumn = "hash";

    /// <summary>The input seed of a failure that names none.</summary>
    internal const int DefaultInputSeed = 0;

    /// <summary>
    /// Reads the request from the failure <paramref name="details"/> of the stage <paramref name="stage"/>, or returns
    /// <see langword="null"/> when its input seed is not an integer, which the reducer would reject.
    /// </summary>
    internal static ReduceRequest? From(JsonObject details, string stage)
    {
        int inputSeed = DefaultInputSeed;
        if (details["inputSeed"] is JsonNode seedNode &&
            !int.TryParse(seedNode.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
        {
            return null;
        }

        bool cudaFailure = details["failingPaths"] is JsonArray paths && ContainsCuda(paths);
        bool cudaStage = stage.StartsWith("cuda_", StringComparison.Ordinal);
        return new ReduceRequest(
            FailingPaths: JoinFailingPaths(details)
                ?? FuzzRunProcess.GetString(details, "dialect")
                ?? DefaultFailingPaths,
            FailureKind: FuzzRunProcess.GetString(details, "failureKind")
                ?? FuzzRunProcess.GetString(details, "status")
                ?? DefaultFailureKind,
            Column: FuzzRunProcess.GetString(details, "column") ?? DefaultColumn,
            InputSeed: inputSeed,
            Profile: (cudaStage || cudaFailure ? FuzzProfile.All : FuzzProfile.Cpu).ToCode());
    }

    private static string? JoinFailingPaths(JsonObject details)
    {
        if (details["failingPaths"] is not JsonArray { Count: > 0 } array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var item in array)
        {
            parts.Add(item?.ToString() ?? string.Empty);
        }

        return string.Join(",", parts);
    }

    private static bool ContainsCuda(JsonArray paths)
    {
        foreach (var item in paths)
        {
            if (string.Equals(item?.ToString(), "cuda", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
