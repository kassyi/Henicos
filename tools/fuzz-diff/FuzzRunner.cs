using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>The counters of one <c>fuzz-diff run</c>, shared by the batch loop and the finding recorder.</summary>
internal sealed class FuzzRunTotals
{
    internal int Batches { get; set; }

    internal int Kernels { get; set; }

    internal int Failures { get; set; }

    internal int Suspected1078 { get; set; }

    internal int Hangs { get; set; }

    internal int OtherFindings { get; set; }

    internal long ReductionMs { get; set; }
}

/// <summary>
/// The batch loop of <c>fuzz-diff run</c>: builds the prerequisites, then generates, builds, runs and records one batch
/// after another until the batch count or the time limit is reached, and writes <c>summary.json</c>.
/// </summary>
internal sealed class FuzzRunner(RunCommandOptions options)
{
    private static readonly string[] s_prerequisiteDlls =
    [
        "src/Core/Kassyi.GSharp.Core/Kassyi.GSharp.Core.csproj|src/Core/Kassyi.GSharp.Core/bin/{os}/Debug/net10.0/Kassyi.GSharp.Core.dll",
        "src/Runtime/Kassyi.GSharp.Runtime/Kassyi.GSharp.Runtime.csproj|src/Runtime/Kassyi.GSharp.Runtime/bin/{os}/Debug/net10.0/Kassyi.GSharp.Runtime.dll",
        "src/Compiler/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.csproj|src/Compiler/Kassyi.GSharp.Compiler/bin/{os}/Debug/netstandard2.0/Kassyi.GSharp.Compiler.dll",
        "src/Compiler/Kassyi.GSharp.Compiler.Tasks/Kassyi.GSharp.Compiler.Tasks.csproj|src/Compiler/Kassyi.GSharp.Compiler.Tasks/bin/{os}/Debug/netstandard2.0/Kassyi.GSharp.Compiler.Tasks.dll",
    ];

    private readonly FuzzRunTotals _totals = new();

    /// <summary>Runs the fuzzer and returns the exit code.</summary>
    internal int Run()
    {
        EnsurePrerequisitesBuilt();

        string? cudaArch = options.Profile == "G" ? FuzzRunProcess.DetectHostCudaArch(options.RepoRoot) : null;
        if (cudaArch is not null)
        {
            Console.Out.WriteLine($"[fuzz-diff] Detected host CUDA architecture: {cudaArch}");
        }

        var catalog = KnownIssueFilter.LoadEntries();
        var activeKnownIssues = SelectActiveKnownIssues(catalog);
        if (activeKnownIssues.Count > 0)
        {
            var names = new List<string>();
            foreach (var entry in activeKnownIssues)
            {
                names.Add($"#{entry.Issue}");
            }

            Console.Out.WriteLine($"[fuzz-diff] Active known-issue switches on profile {options.Profile}: {string.Join(", ", names)}");
        }

        Directory.CreateDirectory(options.WorkDir);
        Directory.CreateDirectory(options.OutDir);

        var executor = new FuzzBatchExecutor(options, cudaArch);
        var recorder = new FuzzFindingRecorder(options, Path.Combine(options.WorkDir, "findings"), catalog);
        var stopwatch = Stopwatch.StartNew();
        double timeLimitMs = options.Minutes * 60 * 1000;

        while (true)
        {
            if (options.Batches is int batchLimit)
            {
                if (_totals.Batches >= batchLimit)
                {
                    break;
                }
            }
            else if (_totals.Batches > 0 && stopwatch.ElapsedMilliseconds >= timeLimitMs)
            {
                break;
            }

            string seedHex = FuzzRandom.FormatSeed(unchecked(options.FuzzSeed + (ulong)_totals.Batches));
            long batchStartMs = stopwatch.ElapsedMilliseconds;

            var batch = executor.RunSingleBatch(_totals.Batches, seedHex);
            recorder.Handle(seedHex, batch, _totals);

            _totals.Batches++;
            _totals.Kernels += options.Metamorphic ? options.BatchSize * 3 : options.BatchSize;
            ReportBatch(seedHex, stopwatch.ElapsedMilliseconds, batchStartMs);
        }

        WriteSummary(activeKnownIssues, stopwatch.ElapsedMilliseconds);
        return 0;
    }

    private void ReportBatch(string seedHex, long nowMs, long batchStartMs)
    {
        double batchSec = (nowMs - batchStartMs) / 1000.0;
        double totalSec = nowMs / 1000.0;
        double pureEvalSec = Math.Max((nowMs - _totals.ReductionMs) / 1000.0, 0.001);
        long kernelsPerHour = RoundPerHour(_totals.Kernels, Math.Max(totalSec, 0.001));
        long pureKernelsPerHour = RoundPerHour(_totals.Kernels, pureEvalSec);
        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[fuzz-diff] Batch #{_totals.Batches} (seed={seedHex}, profile={options.Profile}, size={options.BatchSize}) finished in {batchSec:F2}s | total={_totals.Kernels} kernels ({kernelsPerHour} kernels/hr overall, {pureKernelsPerHour} kernels/hr eval), findings={_totals.Failures} (#1078={_totals.Suspected1078}, hangs={_totals.Hangs}, other={_totals.OtherFindings})"));
    }

    private void WriteSummary(List<KnownIssueEntry> activeKnownIssues, long elapsedMs)
    {
        double elapsedSec = Math.Round(elapsedMs / 1000.0, 2, MidpointRounding.AwayFromZero);
        double pureEvalSec = Math.Max((elapsedMs - _totals.ReductionMs) / 1000.0, 0.001);
        long kernelsPerHour = RoundPerHour(_totals.Kernels, Math.Max(elapsedMs / 1000.0, 0.001));
        long pureKernelsPerHour = RoundPerHour(_totals.Kernels, pureEvalSec);

        var allowed = new JsonArray();
        foreach (int issue in options.AllowedIssues)
        {
            allowed.Add(issue);
        }

        var known = new JsonArray();
        foreach (var entry in activeKnownIssues)
        {
            var paths = new JsonArray();
            foreach (string path in entry.Paths)
            {
                paths.Add(path);
            }

            known.Add(new JsonObject
            {
                ["issue"] = entry.Issue,
                ["kind"] = entry.Kind,
                ["summary"] = entry.Summary,
                ["paths"] = paths,
            });
        }

        var summary = new JsonObject
        {
            ["profile"] = options.Profile,
            ["allowedIssues"] = allowed,
            ["swarmFocus"] = options.SwarmFocus ?? "default",
            ["metamorphic"] = options.Metamorphic,
            ["activeKnownIssues"] = known,
            ["batches"] = _totals.Batches,
            ["batchSize"] = options.BatchSize,
            ["totalKernels"] = _totals.Kernels,
            ["elapsedSeconds"] = elapsedSec,
            ["reductionSeconds"] = Math.Round(_totals.ReductionMs / 1000.0, 2, MidpointRounding.AwayFromZero),
            ["kernelsPerHour"] = kernelsPerHour,
            ["pureEvalKernelsPerHour"] = pureKernelsPerHour,
            ["findingsCount"] = _totals.Failures,
            ["suspected1078Count"] = _totals.Suspected1078,
            ["hangCount"] = _totals.Hangs,
            ["otherFindingsCount"] = _totals.OtherFindings,
        };
        FuzzFindingRecorder.WriteJson(Path.Combine(options.WorkDir, "summary.json"), summary);

        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[fuzz-diff] Completed {_totals.Batches} batches ({_totals.Kernels} kernels) in {elapsedMs / 1000.0:F2}s ({kernelsPerHour} kernels/hr overall, {pureKernelsPerHour} kernels/hr eval). Findings: {_totals.Failures} (#1078 suspected: {_totals.Suspected1078}, hangs: {_totals.Hangs}, other: {_totals.OtherFindings})."));
    }

    private static long RoundPerHour(int kernels, double seconds) =>
        (long)Math.Round(kernels / seconds * 3600, MidpointRounding.AwayFromZero);

    private List<KnownIssueEntry> SelectActiveKnownIssues(IReadOnlyList<KnownIssueEntry> catalog)
    {
        var active = new List<KnownIssueEntry>();
        foreach (var entry in catalog)
        {
            if (!options.AllowedIssues.Contains(entry.Issue) && HasProfile(entry))
            {
                active.Add(entry);
            }
        }

        return active;
    }

    private bool HasProfile(KnownIssueEntry entry)
    {
        foreach (string profile in entry.Profiles)
        {
            if (profile == options.Profile)
            {
                return true;
            }
        }

        return false;
    }

    // Core, Runtime, the compiler and its MSBuild tasks are built only when their DLL is missing; the batch projects
    // reference the built DLLs with --no-dependencies.
    private void EnsurePrerequisitesBuilt()
    {
        foreach (string item in s_prerequisiteDlls)
        {
            string[] parts = item.Split('|');
            string dll = Path.Combine(options.RepoRoot, parts[1].Replace("{os}", FuzzHostOs.Name, StringComparison.Ordinal));
            if (File.Exists(dll))
            {
                continue;
            }

            string project = Path.Combine(options.RepoRoot, parts[0]);
            var result = FuzzRunProcess.Run("dotnet", FuzzBatchExecutor.BuildArguments(project, restoredAssets: HasAssets(project)), options.RepoRoot, FuzzBatchExecutor.BuildTimeoutMs);
            if (result.Status != 0)
            {
                Console.Error.WriteLine(result.Stdout);
                Console.Error.WriteLine(result.Stderr);
                throw new FuzzRunAbortException($"Failed to build prerequisite project: {project}");
            }
        }
    }

    private static bool HasAssets(string projectPath) =>
        File.Exists(Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, "obj", FuzzHostOs.Name, "project.assets.json"));
}
