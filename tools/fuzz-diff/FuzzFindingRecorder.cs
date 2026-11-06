using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Records the failures of a batch under <c>&lt;work-dir&gt;/findings</c>: classifies a failure against the known
/// issues, reduces and promotes it through <c>fuzz-diff reduce</c> and <c>fuzz-diff promote</c> (once per kernel), and
/// writes <c>finding.json</c>, the batch sources and one line of <c>occurrences.jsonl</c>.
/// </summary>
internal sealed class FuzzFindingRecorder(RunCommandOptions options, string findingsDir, IReadOnlyList<KnownIssueEntry> catalog)
{
    // A reduction runs up to 16 rounds, each with its own batch build and run; the reducer times out each process
    // itself, so this only stops a reducer that hangs between them.
    private const int ReduceTimeoutMs = 30 * 60_000;
    private const int PromoteTimeoutMs = 60_000;

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions s_indentedJson = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions s_lineJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HashSet<string> _reducedKernels = new(StringComparer.Ordinal);

    /// <summary>Writes a JSON document in the layout of <c>finding.json</c> and <c>summary.json</c>: indented, with a final line break.</summary>
    internal static void WriteJson(string path, JsonNode node) =>
        File.WriteAllText(path, node.ToJsonString(s_indentedJson) + "\n", s_utf8NoBom);

    /// <summary>Records every failure of <paramref name="batch"/> and adds it to <paramref name="totals"/>.</summary>
    internal void Handle(string seedHex, FuzzBatchResult batch, FuzzRunTotals totals)
    {
        foreach (var failure in batch.StageFailures)
        {
            totals.Failures++;
            if (FuzzRunProcess.GetString(failure.Details, "status") == "hang")
            {
                totals.Hangs++;
            }
            else
            {
                totals.OtherFindings++;
            }

            Record(seedHex, failure, batch);
        }

        foreach (var failure in batch.KernelFailures)
        {
            totals.Failures++;
            totals.ReductionMs += Record(seedHex, failure, batch);
            if (failure.Details["suspectedIssue"]?.GetValue<int>() == 1078)
            {
                totals.Suspected1078++;
            }
            else if (FuzzRunProcess.GetString(failure.Details, "status") == "hang")
            {
                totals.Hangs++;
            }
            else
            {
                totals.OtherFindings++;
            }
        }
    }

    // Marks the failure with the known issue whose classify-only paths are exactly the failing paths.
    private void Classify(JsonObject details)
    {
        string? status = FuzzRunProcess.GetString(details, "status");
        if (status is "compile_error" or "hang")
        {
            return;
        }

        var paths = new List<string>();
        if (details["failingPaths"] is JsonArray array)
        {
            foreach (var item in array)
            {
                paths.Add((item?.ToString() ?? string.Empty).ToLowerInvariant());
            }
        }

        foreach (var entry in catalog)
        {
            if (entry.ClassifyOnlyPaths is not { Count: > 0 } expected || paths.Count != expected.Count)
            {
                continue;
            }

            bool allPresent = true;
            foreach (string path in expected)
            {
                if (!paths.Contains(path.ToLowerInvariant()))
                {
                    allPresent = false;
                    break;
                }
            }

            if (allPresent)
            {
                details["suspectedIssue"] = entry.Issue;
                details["suspectedIssueNote"] = $"#{entry.Issue} suspected ({string.Join(",", expected)} only; {entry.Summary})";
                return;
            }
        }
    }

    private long Record(string seedHex, FuzzStageFailure failure, FuzzBatchResult batch)
    {
        JsonObject details = failure.Details;
        Directory.CreateDirectory(findingsDir);
        Classify(details);

        string findingDir = Path.Combine(findingsDir, $"{seedHex}_{failure.Stage}");
        long reductionMs = 0;

        string? kernel = FuzzRunProcess.GetString(details, "kernel");
        string? dedupKey = string.IsNullOrEmpty(kernel) ? null : $"{seedHex}:{kernel}";
        bool knownClassified = details["suspectedIssue"] is JsonNode suspected && !options.AllowedIssues.Contains(suspected.GetValue<int>());
        bool shouldReduce = !options.NoReduce && !string.IsNullOrEmpty(kernel) &&
            FuzzRunProcess.GetString(details, "status") != "hang" && !knownClassified &&
            (dedupKey is null || !_reducedKernels.Contains(dedupKey));

        if (shouldReduce && kernel is not null)
        {
            if (dedupKey is not null)
            {
                _reducedKernels.Add(dedupKey);
            }

            var stopwatch = Stopwatch.StartNew();
            findingDir = ReduceAndPromote(seedHex, failure.Stage, kernel, details, batch.KernelsSource, findingDir);
            reductionMs = stopwatch.ElapsedMilliseconds;
        }

        Directory.CreateDirectory(findingDir);
        File.WriteAllText(Path.Combine(findingDir, "FuzzBatchKernels.cu.cs"), batch.KernelsSource, s_utf8NoBom);
        File.WriteAllText(Path.Combine(findingDir, "FuzzBatchCases.cs"), batch.CasesSource, s_utf8NoBom);
        WriteJson(Path.Combine(findingDir, "finding.json"), details);
        File.AppendAllText(Path.Combine(findingsDir, "occurrences.jsonl"), details.ToJsonString(s_lineJson) + "\n", s_utf8NoBom);
        return reductionMs;
    }

    // Runs `fuzz-diff reduce` on the kernel and, when that succeeds, `fuzz-diff promote` on the reduced source.
    // Returns the finding directory: the promoted repro's directory when there is one, else <paramref name="defaultDir"/>.
    private string ReduceAndPromote(string seedHex, string stage, string kernel, JsonObject details, string kernelsSource, string defaultDir)
    {
        string findingDir = defaultDir;
        string sourcePath = Path.Combine(findingsDir, $"_tmp_{seedHex}_{kernel}.cu.cs");
        string reducedPath = Path.Combine(findingsDir, $"_tmp_{seedHex}_{kernel}.reduced.cu.cs");
        try
        {
            File.WriteAllText(sourcePath, kernelsSource, s_utf8NoBom);

            string failingPaths = JoinFailingPaths(details) ?? FuzzRunProcess.GetString(details, "dialect") ?? "Native,Cuda";
            string failureKind = FuzzRunProcess.GetString(details, "failureKind") ?? FuzzRunProcess.GetString(details, "status") ?? "CellMismatch";
            string column = FuzzRunProcess.GetString(details, "column") ?? "hash";
            string inputSeed = details["inputSeed"] is JsonNode seedNode ? seedNode.ToString() : "0";
            bool cudaFailure = details["failingPaths"] is JsonArray paths && ContainsCuda(paths);
            string reduceProfile = stage.StartsWith("cuda_", StringComparison.Ordinal) || cudaFailure ? "G" : "C";

            string selfDll = typeof(Program).Assembly.Location;
            var reduceArgs = new List<string>
            {
                "exec", selfDll, "reduce",
                "--source", sourcePath, "--fuzz-seed", seedHex, "--kernel", kernel, "--input-seed", inputSeed,
                "--profile", reduceProfile, "--work-dir", Path.Combine(options.WorkDir, "_reduce"),
                "--repo-root", options.RepoRoot, "--failing-paths", failingPaths, "--failure-kind", failureKind,
                "--column", column, "--out", reducedPath,
            };
            if (options.NoGpuLock || FuzzRunProcess.IsAlreadyInsideGpuLock(options.RepoRoot))
            {
                reduceArgs.Add("--no-gpu-lock");
            }

            var reduce = FuzzRunProcess.Run("dotnet", reduceArgs, options.RepoRoot, ReduceTimeoutMs);
            if (reduce.Status != 0 || !File.Exists(reducedPath))
            {
                return findingDir;
            }

            JsonObject reduced = FindEvent(FuzzRunProcess.ParseJsonLines(reduce.Stdout), "reduced") ?? [];
            details["fingerprint"] = reduced["fingerprint"]?.DeepClone();
            CopyIfPresent(reduced, "originalStatements", details);
            CopyIfPresent(reduced, "reducedStatements", details);

            var promoteArgs = new List<string>
            {
                "exec", selfDll, "promote",
                "--source", reducedPath, "--fuzz-seed", seedHex, "--kernel", kernel, "--input-seed", inputSeed,
                "--failing-paths", failingPaths, "--failure-kind", failureKind, "--column", column,
                "--findings-dir", findingsDir,
            };
            var promote = FuzzRunProcess.Run("dotnet", promoteArgs, options.RepoRoot, PromoteTimeoutMs);
            JsonObject? promoted = FindEvent(FuzzRunProcess.ParseJsonLines(promote.Stdout), "promoted");
            string? reproPath = promoted is null ? null : FuzzRunProcess.GetString(promoted, "reproKernelPath");
            if (promoted is not null && !string.IsNullOrEmpty(reproPath))
            {
                findingDir = Path.GetDirectoryName(reproPath) ?? findingDir;
                if (promoted["fingerprint"] is JsonNode fingerprint)
                {
                    details["fingerprint"] = fingerprint.DeepClone();
                }

                details["reproKernelPath"] = reproPath;
                details["knownDivergenceEntry"] = promoted["knownDivergenceEntry"]?.DeepClone();
            }
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(reducedPath);
        }

        return findingDir;
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
}
