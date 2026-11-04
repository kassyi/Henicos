using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>One kernel (or the code outside every kernel) of a batch whose generated code differs.</summary>
/// <param name="Status">Always <c>codegen_diff</c>.</param>
/// <param name="FailureKind">Always <c>CodegenDiff</c>.</param>
/// <param name="CompareBase">The base ref.</param>
/// <param name="BatchFuzzSeed">The fuzz seed of the batch.</param>
/// <param name="Kernel">The kernel name, or <see cref="CodegenComparison.NoKernel"/>.</param>
/// <param name="Differences">The first differing line of each output that differs.</param>
/// <param name="Fingerprint">The fingerprint of the reduced kernel, when it was reduced.</param>
/// <param name="OriginalStatements">The statement count before reduction.</param>
/// <param name="ReducedStatements">The statement count after reduction.</param>
/// <param name="Iterations">The candidates the reduction compiled.</param>
/// <param name="ReusedFromKernel">The kernel whose reduction this finding reuses, because both show the same change.</param>
internal sealed record CompareFinding(
    string Status,
    string FailureKind,
    string CompareBase,
    string BatchFuzzSeed,
    string Kernel,
    IReadOnlyList<CodegenDifference> Differences,
    string? Fingerprint,
    int? OriginalStatements,
    int? ReducedStatements,
    int? Iterations,
    string? ReusedFromKernel);

/// <summary>Contents of <c>compare-summary.json</c>.</summary>
internal sealed record CompareSummary(
    string Mode,
    string CompareBase,
    bool ExpectChange,
    bool Metamorphic,
    int Batches,
    int BatchSize,
    int TotalKernels,
    int TotalDifferences,
    double ElapsedSeconds,
    IReadOnlyList<CompareFinding> Differences);

/// <summary>
/// Runs <c>fuzz-diff compare</c>: builds the compiler of the working tree and of a worktree of the base ref, then for
/// each batch compiles the generated kernels with both and reports, reduces and records every kernel whose output
/// differs.
/// </summary>
internal sealed class CodegenCompareRunner
{
    private static readonly JsonSerializerOptions s_indentedJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions s_lineJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly CompareCommandOptions _options;
    private readonly CompareSide _head;
    private readonly CompareSide _base;
    private readonly string _findingsDir;
    private readonly Dictionary<string, (string Kernel, InProcessReduction Reduction)> _reducedBySignature = new(StringComparer.Ordinal);
    private readonly List<CompareFinding> _findings = [];

    private CodegenCompareRunner(CompareCommandOptions options, CompareSide head, CompareSide baseSide)
    {
        _options = options;
        _head = head;
        _base = baseSide;
        _findingsDir = Path.Combine(options.WorkDir, "findings");
    }

    /// <summary>Runs the comparison and returns the exit code.</summary>
    internal static int Run(CompareCommandOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        string runDir = Path.Combine(
            options.WorkDir,
            "compare",
            string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}"));

        Log($"Building the compiler of the working tree ({options.RepoRoot})...");
        CompareTools.BuildCompilerPrerequisites(options.RepoRoot);

        int totalKernels;
        CodegenCompareRunner runner;
        try
        {
            Log($"Creating a worktree of '{options.BaseRef}' at {Path.Combine(runDir, "base-worktree")}...");
            using var worktree = BaseWorktree.Create(options.RepoRoot, options.BaseRef, Path.Combine(runDir, "base-worktree"));
            Log($"Building the compiler of '{options.BaseRef}'...");
            CompareTools.BuildCompilerPrerequisites(worktree.Root);

            runner = new CodegenCompareRunner(
                options,
                new CompareSide(Path.Combine(runDir, "head"), options.RepoRoot),
                new CompareSide(Path.Combine(runDir, "base"), worktree.Root));
            totalKernels = runner.RunBatches();
        }
        finally
        {
            DeleteRunDirectory(runDir);
        }

        return runner.Finish(totalKernels, stopwatch.Elapsed.TotalSeconds);
    }

    private static void Log(string message) => Console.Out.WriteLine($"[fuzz-compare] {message}");

    // The worktree is removed first (by its Dispose); this removes the compare projects of the run.
    private static void DeleteRunDirectory(string runDir)
    {
        if (!Directory.Exists(runDir))
        {
            return;
        }

        try
        {
            Directory.Delete(runDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[fuzz-compare] Could not delete {runDir}: {ex.Message}");
        }
    }

    private int RunBatches()
    {
        int totalKernels = 0;
        for (int batchIndex = 0; batchIndex < _options.Batches; batchIndex++)
        {
            var batchStart = Stopwatch.StartNew();
            ulong seed = unchecked(_options.Generation.FuzzSeed + (ulong)batchIndex);
            string seedHex = FuzzRandom.FormatSeed(seed);
            var batch = new ScalarFuzzGenerator(_options.Generation with { FuzzSeed = seed }).GenerateBatch();
            totalKernels += batch.Kernels.Count;

            var differences = new List<CodegenDifference>();
            foreach (string dialect in _options.Dialects)
            {
                differences.AddRange(CompareSource(dialect, batch.KernelsSource, throwOnBuildFailure: true));
            }

            int findingsBefore = _findings.Count;
            foreach (var group in GroupByKernel(differences))
            {
                RecordFinding(seedHex, group.Key, group.Value, batch.KernelsSource);
            }

            Log(string.Create(
                CultureInfo.InvariantCulture,
                $"Batch #{batchIndex + 1}/{_options.Batches} (seed={seedHex}, kernels={batch.Kernels.Count}) finished in {batchStart.Elapsed.TotalSeconds:F2}s | diffs_in_batch={_findings.Count - findingsBefore}, total_diffs={_findings.Count}"));
        }

        return totalKernels;
    }

    private int Finish(int totalKernels, double elapsedSeconds)
    {
        var summary = new CompareSummary(
            "compare-base",
            _options.BaseRef,
            _options.ExpectChange,
            _options.Generation.Metamorphic,
            _options.Batches,
            _options.Generation.BatchSize,
            totalKernels,
            _findings.Count,
            Math.Round(elapsedSeconds, 2),
            _findings);
        Directory.CreateDirectory(_options.WorkDir);
        File.WriteAllText(
            Path.Combine(_options.WorkDir, "compare-summary.json"),
            JsonSerializer.Serialize(summary, s_indentedJson) + "\n",
            s_utf8NoBom);
        Log(string.Create(
            CultureInfo.InvariantCulture,
            $"Completed {_options.Batches} batches ({totalKernels} kernels) vs '{_options.BaseRef}' in {elapsedSeconds:F2}s. Differences: {_findings.Count}."));

        return _findings.Count > 0 && !_options.ExpectChange ? CompareCommand.ExitDifference : 0;
    }

    // Builds `kernelsSource` on both sides and compares. During reduction a candidate that does not build is not a
    // difference; in the batch loop a build failure stops the run, because the comparison would be incomplete.
    private List<CodegenDifference> CompareSource(string dialect, string kernelsSource, bool throwOnBuildFailure)
    {
        var baseResult = _base.Build(dialect, kernelsSource);
        var headResult = _head.Build(dialect, kernelsSource);
        if (baseResult.Succeeded && headResult.Succeeded)
        {
            return CodegenComparison.Compare(dialect, baseResult.Outputs, headResult.Outputs);
        }

        if (throwOnBuildFailure)
        {
            throw new CompareInfrastructureException(
                $"The generator build failed ({dialect}).{Environment.NewLine}Base:{Environment.NewLine}{baseResult.Log}{Environment.NewLine}Working tree:{Environment.NewLine}{headResult.Log}");
        }

        return [];
    }

    private static SortedDictionary<string, List<CodegenDifference>> GroupByKernel(List<CodegenDifference> differences)
    {
        var groups = new SortedDictionary<string, List<CodegenDifference>>(StringComparer.Ordinal);
        foreach (var difference in differences)
        {
            string key = difference.Kernel ?? CodegenComparison.NoKernel;
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(difference);
        }

        return groups;
    }

    private void RecordFinding(string seedHex, string kernel, List<CodegenDifference> differences, string kernelsSource)
    {
        Log($"DIFF seed={seedHex} kernel={kernel}");
        foreach (var d in differences)
        {
            Console.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  [{d.Dialect} {d.Output}] {d.Scope} line {d.Line} (file line {d.FileLine}):{Environment.NewLine}    base ({_options.BaseRef}): {d.BaseText}{Environment.NewLine}    working tree: {d.HeadText}"));
        }

        string findingDir = Path.Combine(_findingsDir, $"{seedHex}_compare_{SanitizeForPath(kernel)}");
        Directory.CreateDirectory(findingDir);
        File.WriteAllText(Path.Combine(findingDir, "FuzzBatchKernels.cu.cs"), kernelsSource, s_utf8NoBom);

        var (reduction, reusedFrom) = _options.NoReduce ? (null, null) : Reduce(kernel, differences[0], kernelsSource);
        if (reduction is not null)
        {
            File.WriteAllText(Path.Combine(findingDir, "ReproKernel.cu.cs"), reduction.ReducedSource, s_utf8NoBom);
        }

        var finding = new CompareFinding(
            "codegen_diff",
            "CodegenDiff",
            _options.BaseRef,
            seedHex,
            kernel,
            differences,
            reduction?.Fingerprint,
            reduction?.OriginalStatements,
            reduction?.ReducedStatements,
            reduction?.Iterations,
            reusedFrom);
        File.WriteAllText(Path.Combine(findingDir, "finding.json"), JsonSerializer.Serialize(finding, s_indentedJson) + "\n", s_utf8NoBom);
        File.AppendAllText(Path.Combine(_findingsDir, "occurrences.jsonl"), JsonSerializer.Serialize(finding, s_lineJson) + "\n", s_utf8NoBom);
        _findings.Add(finding);
    }

    // Reduces the kernel while the same output of the same dialect still differs. A difference outside every kernel
    // is reduced on the whole batch. The same change in another kernel reuses the first reduction.
    private (InProcessReduction? Reduction, string? ReusedFrom) Reduce(string kernel, CodegenDifference first, string kernelsSource)
    {
        string signature = CodegenComparison.GetSignature(first);
        if (_reducedBySignature.TryGetValue(signature, out var cached))
        {
            return (cached.Reduction, cached.Kernel);
        }

        var deadline = Stopwatch.StartNew();
        var limit = TimeSpan.FromMinutes(_options.ReduceMinutes);
        bool StillDiffers(string candidate) =>
            deadline.Elapsed < limit &&
            HasMatchingDifference(CompareSource(first.Dialect, candidate, throwOnBuildFailure: false), first);

        string start = first.Kernel is null
            ? kernelsSource
            : ReduceCommand.IsolateSingleKernelCompilationUnit(kernelsSource, first.Kernel);
        if (!StillDiffers(start))
        {
            Log($"  [reduce] {kernel}: the isolated kernel does not show the difference; not reduced.");
            return (null, null);
        }

        var reduction = ReduceCommand.ReduceInProcess(start, StillDiffers, first.Dialect, "CodegenDiff");
        _reducedBySignature[signature] = (kernel, reduction);
        Log(string.Create(
            CultureInfo.InvariantCulture,
            $"  [reduced] {kernel}: {reduction.OriginalStatements} -> {reduction.ReducedStatements} statements ({reduction.Iterations} iterations)"));
        return (reduction, null);
    }

    private static bool HasMatchingDifference(List<CodegenDifference> differences, CodegenDifference original)
    {
        foreach (var d in differences)
        {
            if (string.Equals(d.Output, original.Output, StringComparison.Ordinal) &&
                (original.Kernel is null || string.Equals(d.Kernel, original.Kernel, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeForPath(string name) =>
        string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_';
            }
        });
}
