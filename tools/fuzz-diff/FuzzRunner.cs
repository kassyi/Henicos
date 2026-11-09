using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>The kernels-per-hour rates of a run.</summary>
/// <param name="Overall">Kernels per hour over the whole elapsed time.</param>
/// <param name="PureEval">Kernels per hour over the elapsed time without the reductions.</param>
internal readonly record struct FuzzRunRates(long Overall, long PureEval);

/// <summary>The counters of one <c>fuzz-diff run</c>, shared by the batch loop and the finding recorder.</summary>
internal sealed class FuzzRunTotals
{
    internal int Batches { get; set; }

    internal int Kernels { get; set; }

    internal int Failures { get; set; }

    internal SortedDictionary<int, int> SuspectedIssues { get; } = [];

    internal int Hangs { get; set; }

    internal int OtherFindings { get; set; }

    internal long ReductionMs { get; set; }

    /// <summary>Counts one finding: per known issue when classified so, else as a hang or another finding.</summary>
    internal void Count(JsonObject details)
    {
        Failures++;
        if (details["suspectedIssue"]?.GetValue<int>() is int issue)
        {
            SuspectedIssues[issue] = SuspectedIssues.GetValueOrDefault(issue) + 1;
        }
        else if (FuzzRunProcess.GetString(details, "status") == "hang")
        {
            Hangs++;
        }
        else
        {
            OtherFindings++;
        }
    }

    /// <summary>Returns the rates after <paramref name="elapsedMs"/>; each time is at least one millisecond.</summary>
    internal FuzzRunRates Rates(long elapsedMs)
    {
        double totalSec = Math.Max(elapsedMs / 1000.0, 0.001);
        double pureEvalSec = Math.Max((elapsedMs - ReductionMs) / 1000.0, 0.001);
        return new FuzzRunRates(PerHour(totalSec), PerHour(pureEvalSec));
    }

    private long PerHour(double seconds) =>
        (long)Math.Round(Kernels / seconds * 3600, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The batch loop of <c>fuzz-diff run</c>: builds the prerequisites, then generates, builds, runs and records one batch
/// after another until the batch count or the time limit is reached, and writes <c>summary.json</c>.
/// </summary>
internal sealed class FuzzRunner(RunCommandOptions options)
{
    // A metamorphic batch runs each generated kernel and its variants.
    private const int KernelsPerMetamorphicBase = 1 + FuzzOptions.DefaultMetamorphicVariantsPerKernel;

    // The projects the batch projects reference with --no-dependencies, so they are built first.
    private static readonly string[] s_prerequisiteProjects =
    [
        "src/Core/Kassyi.GSharp.Core/Kassyi.GSharp.Core.csproj",
        "src/Runtime/Kassyi.GSharp.Runtime/Kassyi.GSharp.Runtime.csproj",
        "src/Compiler/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.csproj",
        "src/Compiler/Kassyi.GSharp.Compiler.Tasks/Kassyi.GSharp.Compiler.Tasks.csproj",
    ];

    private readonly FuzzRunTotals _totals = new();

    /// <summary>Runs the fuzzer and returns the exit code.</summary>
    internal int Run()
    {
        BuildPrerequisites();

        string repoRoot = options.Paths.RepoRoot;
        string? cudaArch = options.Profile == FuzzProfile.All ? FuzzRunProcess.DetectHostCudaArch(repoRoot) : null;
        if (cudaArch is not null)
        {
            Console.Out.WriteLine($"[fuzz-diff] Detected host CUDA architecture: {cudaArch}");
        }

        var catalog = KnownIssueFilter.LoadEntries();
        var activeKnownIssues = SelectActiveKnownIssues(catalog);
        ReportActiveKnownIssues(activeKnownIssues);

        Directory.CreateDirectory(options.Paths.WorkDir);
        Directory.CreateDirectory(options.Paths.OutDir);

        var executor = new FuzzBatchExecutor(options, cudaArch);
        var recorder = new FuzzFindingRecorder(options, Path.Combine(options.Paths.WorkDir, "findings"), catalog);
        var stopwatch = Stopwatch.StartNew();
        int batchSize = options.Generation.BatchSize;
        int kernelsPerBatch = options.Generation.Metamorphic ? batchSize * KernelsPerMetamorphicBase : batchSize;

        while (!IsDone(stopwatch.ElapsedMilliseconds))
        {
            string seedHex = FuzzRandom.FormatSeed(unchecked(options.Generation.FuzzSeed + (ulong)_totals.Batches));
            long batchStartMs = stopwatch.ElapsedMilliseconds;

            var batch = executor.RunSingleBatch(_totals.Batches, seedHex);
            recorder.Handle(seedHex, batch, _totals);

            _totals.Batches++;
            _totals.Kernels += kernelsPerBatch;
            ReportBatch(seedHex, stopwatch.ElapsedMilliseconds, batchStartMs);
        }

        WriteSummary(activeKnownIssues, stopwatch.ElapsedMilliseconds);
        return 0;
    }

    // The run ends at the batch count, or without one, after at least one batch, at the time limit.
    private bool IsDone(long elapsedMs) =>
        options.Limits.Batches is int batchLimit
            ? _totals.Batches >= batchLimit
            : _totals.Batches > 0 && elapsedMs >= options.Limits.Minutes * 60 * 1000;

    private void ReportActiveKnownIssues(List<KnownIssueEntry> activeKnownIssues)
    {
        if (activeKnownIssues.Count == 0)
        {
            return;
        }

        var names = new List<string>();
        foreach (var entry in activeKnownIssues)
        {
            names.Add($"#{entry.Issue}");
        }

        string profile = options.Profile.ToCode();
        Console.Out.WriteLine(
            $"[fuzz-diff] Active known-issue switches on profile {profile}: {string.Join(", ", names)}");
    }

    private void ReportBatch(string seedHex, long nowMs, long batchStartMs)
    {
        double batchSec = (nowMs - batchStartMs) / 1000.0;
        var rates = _totals.Rates(nowMs);
        var inv = CultureInfo.InvariantCulture;
        string issueCounts = FormatSuspectedIssuesBatch();
        Console.Out.WriteLine(
            string.Create(inv, $"[fuzz-diff] Batch #{_totals.Batches} (seed={seedHex}, ") +
            string.Create(inv, $"profile={options.Profile.ToCode()}, size={options.Generation.BatchSize}) ") +
            string.Create(inv, $"finished in {batchSec:F2}s | total={_totals.Kernels} kernels ") +
            string.Create(inv, $"({rates.Overall} kernels/hr overall, {rates.PureEval} kernels/hr eval), ") +
            string.Create(inv, $"findings={_totals.Failures} ({issueCounts}") +
            string.Create(inv, $"hangs={_totals.Hangs}, other={_totals.OtherFindings})"));
    }

    private string FormatSuspectedIssuesBatch()
    {
        var nonZero = new List<string>();
        foreach ((int issue, int count) in _totals.SuspectedIssues)
        {
            if (count > 0)
            {
                nonZero.Add(string.Create(CultureInfo.InvariantCulture, $"#{issue}={count}"));
            }
        }

        return nonZero.Count > 0 ? string.Join(", ", nonZero) + ", " : string.Empty;
    }

    private void WriteSummary(List<KnownIssueEntry> activeKnownIssues, long elapsedMs)
    {
        var rates = _totals.Rates(elapsedMs);
        var known = new List<FuzzRunSummary.KnownIssue>();
        foreach (var entry in activeKnownIssues)
        {
            known.Add(new FuzzRunSummary.KnownIssue(entry.Issue, entry.Kind, entry.Summary, entry.Paths));
        }

        var suspectedIssuesDict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((int issue, int count) in _totals.SuspectedIssues)
        {
            suspectedIssuesDict[issue.ToString(CultureInfo.InvariantCulture)] = count;
        }

        var generation = options.Generation;
        var summary = new FuzzRunSummary(
            Profile: options.Profile.ToCode(),
            AllowedIssues: generation.AllowedIssues,
            SwarmFocus: generation.SwarmFocus ?? "default",
            Metamorphic: generation.Metamorphic,
            ActiveKnownIssues: known,
            Batches: _totals.Batches,
            BatchSize: generation.BatchSize,
            TotalKernels: _totals.Kernels,
            ElapsedSeconds: RoundSeconds(elapsedMs),
            ReductionSeconds: RoundSeconds(_totals.ReductionMs),
            KernelsPerHour: rates.Overall,
            PureEvalKernelsPerHour: rates.PureEval,
            FindingsCount: _totals.Failures,
            SuspectedIssues: suspectedIssuesDict,
            HangCount: _totals.Hangs,
            OtherFindingsCount: _totals.OtherFindings);
        FuzzJson.WriteRecord(Path.Combine(options.Paths.WorkDir, "summary.json"), summary);

        string summaryIssueText = FormatSuspectedIssuesSummary();
        var inv = CultureInfo.InvariantCulture;
        Console.Out.WriteLine(
            string.Create(inv, $"[fuzz-diff] Completed {_totals.Batches} batches ({_totals.Kernels} kernels) ") +
            string.Create(inv, $"in {elapsedMs / 1000.0:F2}s ({rates.Overall} kernels/hr overall, ") +
            string.Create(inv, $"{rates.PureEval} kernels/hr eval). Findings: {_totals.Failures} ") +
            string.Create(inv, $"({summaryIssueText}hangs: {_totals.Hangs}, ") +
            string.Create(inv, $"other: {_totals.OtherFindings})."));
    }

    private string FormatSuspectedIssuesSummary()
    {
        var nonZero = new List<string>();
        foreach ((int issue, int count) in _totals.SuspectedIssues)
        {
            if (count > 0)
            {
                nonZero.Add(string.Create(CultureInfo.InvariantCulture, $"#{issue} suspected: {count}"));
            }
        }

        return nonZero.Count > 0 ? string.Join(", ", nonZero) + ", " : string.Empty;
    }

    private static double RoundSeconds(long milliseconds) =>
        Math.Round(milliseconds / 1000.0, 2, MidpointRounding.AwayFromZero);

    private List<KnownIssueEntry> SelectActiveKnownIssues(IReadOnlyList<KnownIssueEntry> catalog)
    {
        string profile = options.Profile.ToCode();
        var active = new List<KnownIssueEntry>();
        foreach (var entry in catalog)
        {
            if (!options.Generation.IsAllowed(entry.Issue) && HasProfile(entry, profile))
            {
                active.Add(entry);
            }
        }

        return active;
    }

    private static bool HasProfile(KnownIssueEntry entry, string profile)
    {
        foreach (string entryProfile in entry.Profiles)
        {
            if (entryProfile == profile)
            {
                return true;
            }
        }

        return false;
    }

    // Core, Runtime, the compiler and its MSBuild tasks are built on every run, so that the batches never test a
    // stale compiler; an up-to-date build takes a few seconds. The batch projects reference the built DLLs with
    // --no-dependencies.
    private void BuildPrerequisites()
    {
        foreach (string relativePath in s_prerequisiteProjects)
        {
            string project = Path.Combine(options.Paths.RepoRoot, relativePath);
            var arguments = FuzzBatchExecutor.BuildArguments(project, restoredAssets: false);
            int timeoutMs = FuzzBatchExecutor.BuildTimeoutMs;
            var result = FuzzRunProcess.Run("dotnet", arguments, options.Paths.RepoRoot, timeoutMs);
            if (result.Status != 0)
            {
                Console.Error.WriteLine(result.Stdout);
                Console.Error.WriteLine(result.Stderr);
                throw new FuzzRunAbortException($"Failed to build prerequisite project: {project}");
            }
        }
    }
}

/// <summary>Contents of <c>summary.json</c>, in the order of its keys.</summary>
internal sealed record FuzzRunSummary(
    [property: JsonPropertyName("profile")] string Profile,
    [property: JsonPropertyName("allowedIssues")] IReadOnlyList<int> AllowedIssues,
    [property: JsonPropertyName("swarmFocus")] string SwarmFocus,
    [property: JsonPropertyName("metamorphic")] bool Metamorphic,
    [property: JsonPropertyName("activeKnownIssues")] IReadOnlyList<FuzzRunSummary.KnownIssue> ActiveKnownIssues,
    [property: JsonPropertyName("batches")] int Batches,
    [property: JsonPropertyName("batchSize")] int BatchSize,
    [property: JsonPropertyName("totalKernels")] int TotalKernels,
    [property: JsonPropertyName("elapsedSeconds")] double ElapsedSeconds,
    [property: JsonPropertyName("reductionSeconds")] double ReductionSeconds,
    [property: JsonPropertyName("kernelsPerHour")] long KernelsPerHour,
    [property: JsonPropertyName("pureEvalKernelsPerHour")] long PureEvalKernelsPerHour,
    [property: JsonPropertyName("findingsCount")] int FindingsCount,
    [property: JsonPropertyName("suspectedIssues")] IReadOnlyDictionary<string, int> SuspectedIssues,
    [property: JsonPropertyName("hangCount")] int HangCount,
    [property: JsonPropertyName("otherFindingsCount")] int OtherFindingsCount)
{
    /// <summary>A known issue whose shapes the run avoided.</summary>
    internal sealed record KnownIssue(
        [property: JsonPropertyName("issue")] int Issue,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths);
}
