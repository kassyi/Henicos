#nullable enable
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests for the parts of <c>fuzz-diff run</c> that start no process: option parsing, reading the console lines of a
/// batch and merging the failures of the CPU and CUDA runs.
/// </summary>
public class RunCommandTests
{
    private static readonly string s_repoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fuzz-run-root"));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void TryParseOptions_NoArguments_UsesTheDocumentedDefaults()
    {
        bool parsed = RunCommand.TryParseOptions(
            ["--repo-root", s_repoRoot],
            out var options,
            out bool showHelp,
            out _);

        // mutant: the default profile, minutes or batch size changes
        Assert.True(parsed);
        Assert.False(showHelp);
        Assert.NotNull(options);
        Assert.Equal(FuzzProfile.Cpu, options.Profile);
        Assert.Equal(5, options.Limits.Minutes);
        Assert.Null(options.Limits.Batches);
        Assert.Equal(64, options.Generation.BatchSize);
        Assert.Equal(1UL, options.Generation.FuzzSeed);
        Assert.Equal(Path.Combine(s_repoRoot, "tmp", "differential-fuzz"), options.Paths.WorkDir);
        Assert.Equal(Path.Combine(s_repoRoot, "tmp", "differential-fuzz", "Generated"), options.Paths.OutDir);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void TryParseOptions_EveryOption_IsReadInBothSpellings()
    {
        bool parsed = RunCommand.TryParseOptions(
            [
                "--profile=all", "--minutes", "0.5", "--batches=3", "--batch-size", "8", "--fuzz-seed", "0x2A",
                "--input-seed=4", "--allow-issue", "#1101", "--allow-issue=1101", "--allow-issue", "984",
                "--swarm-focus", "coalesce-assign-value", "--metamorphic", "--compare-base", "origin/main",
                "--expect-change", "--work-dir", "w", "--out-dir=o", "--no-reduce", "--no-gpu-lock",
                "--repo-root", s_repoRoot,
            ],
            out var options,
            out _,
            out string? error);

        // mutant: a spelling is not accepted, a repeated --allow-issue is kept twice, or a relative directory ignores
        // --repo-root
        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal(FuzzProfile.All, options.Profile);
        Assert.Equal(0.5, options.Limits.Minutes);
        Assert.Equal(3, options.Limits.Batches);
        Assert.Equal(8, options.Generation.BatchSize);
        Assert.Equal(0x2AUL, options.Generation.FuzzSeed);
        Assert.Equal(4, options.Generation.InputSeed);
        Assert.Equal([1101, 984], options.Generation.AllowedIssues);
        Assert.Equal("coalesce-assign-value", options.Generation.SwarmFocus);
        Assert.True(options.Generation.Metamorphic);
        Assert.Equal("origin/main", options.CompareBase);
        Assert.True(options.ExpectChange);
        Assert.True(options.NoReduce);
        Assert.True(options.NoGpuLock);
        Assert.Equal(Path.Combine(s_repoRoot, "w"), options.Paths.WorkDir);
        Assert.Equal(Path.Combine(s_repoRoot, "o"), options.Paths.OutDir);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("--profile", "fast")]
    [InlineData("--minutes", "0")]
    [InlineData("--batches", "0")]
    [InlineData("--batch-size", "257")]
    [InlineData("--fuzz-seed", "seed")]
    [InlineData("--allow-issue", "0")]
    [InlineData("--swarm-focus", "unknown")]
    [InlineData("--no-such-option", "1")]
    public void TryParseOptions_InvalidValue_ReportsAnError(string option, string value)
    {
        bool parsed = RunCommand.TryParseOptions([option, value], out var options, out _, out string? error);

        // mutant: a range check is removed
        Assert.False(parsed);
        Assert.Null(options);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void TryParseOptions_MissingValue_ReportsAnError()
    {
        bool parsed = RunCommand.TryParseOptions(["--batches"], out _, out _, out string? error);

        Assert.False(parsed);
        Assert.Equal("--batches requires a value.", error);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("native", FuzzProfile.Cpu)]
    [InlineData("cuda", FuzzProfile.All)]
    [InlineData("GPU", FuzzProfile.All)]
    [InlineData(" managed ", FuzzProfile.Managed)]
    public void TryParseOptions_ProfileAlias_SelectsItsProfile(string value, FuzzProfile expected)
    {
        bool parsed = RunCommand.TryParseOptions(["--profile", value], out var options, out _, out string? error);

        // mutant: an alias of the old script (native, cuda, gpu) is dropped
        Assert.True(parsed, error);
        Assert.Equal(expected, options?.Profile);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("seven")]
    [InlineData("1.5")]
    [InlineData("4294967296")]
    public void TryParseOptions_InvalidInputSeed_ReportsAnError(string value)
    {
        bool parsed = RunCommand.TryParseOptions(["--input-seed", value], out var options, out _, out string? error);

        // mutant: the input seed is not checked, or is parsed as a wider integer
        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal("--input-seed requires an integer.", error);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("--help")]
    [InlineData("-h")]
    public void TryParseOptions_Help_StopsBeforeTheRemainingArguments(string help)
    {
        bool parsed = RunCommand.TryParseOptions(
            ["--batches", "2", help, "--no-such-option"],
            out var options,
            out bool showHelp,
            out string? error);

        // mutant: --help is read as an unknown option, or the arguments after it are still parsed
        Assert.True(parsed, error);
        Assert.True(showHelp);
        Assert.Null(options);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ToCompareOptions_CompareBase_CarriesTheGenerationOptionsAndCompareDefaults()
    {
        RunCommand.TryParseOptions(
            [
                "--compare-base", "origin/main", "--profile", "managed", "--batch-size", "8", "--fuzz-seed", "5",
                "--input-seed", "3", "--allow-issue", "984", "--swarm-focus", "tuple-deconstruct-complex",
                "--metamorphic", "--expect-change", "--no-reduce", "--repo-root", s_repoRoot,
            ],
            out var options,
            out _,
            out _);
        Assert.NotNull(options);

        var compare = RunCommand.ToCompareOptions(options, "origin/main");

        // mutant: a generation option or a flag is not passed on, or the default batch count changes
        Assert.Equal(("origin/main", CompareCommand.DefaultBatches), (compare.BaseRef, compare.Batches));
        var generation = compare.Generation;
        Assert.Equal("M", generation.Profile);
        Assert.Equal((8, 5UL, 3), (generation.BatchSize, generation.FuzzSeed, generation.InputSeed));
        Assert.Equal([984], compare.Generation.AllowedIssues);
        Assert.Equal(FuzzSwarmFocus.TupleDeconstructComplex, compare.Generation.SwarmFocus);
        Assert.True(compare.Generation.Metamorphic && compare.ExpectChange && compare.NoReduce);
        Assert.Equal(["Cpu", "Cuda"], compare.Dialects);
        Assert.Equal(CompareCommand.DefaultReduceMinutes, compare.ReduceMinutes);
        Assert.Equal((options.Paths.WorkDir, s_repoRoot), (compare.WorkDir, compare.RepoRoot));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ParseJsonLines_MixedConsoleOutput_KeepsOnlyJsonObjects()
    {
        var events = FuzzRunProcess.ParseJsonLines(
            "building...\r\n{\"status\":\"failed\",\"kernel\":\"K1\"}\n{not json}\n[1,2]\n  {\"status\":\"ok\"}  \n");

        // mutant: a line that starts and ends with a brace but is not JSON throws, or an array is read as an event
        Assert.Equal(2, events.Count);
        Assert.Equal("K1", FuzzRunProcess.GetString(events[0], "kernel"));
        Assert.Equal("ok", FuzzRunProcess.GetString(events[1], "status"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Merge_SameKernelOnBothRuns_JoinsFailingPathsInPathOrderAndNamesTheCudaStage()
    {
        var cpu = new List<JsonObject> { Failure("K1", "Native"), Failure("K2", "Managed"), Passed("K3") };
        var cuda = new List<JsonObject> { Failure("K1", "Cuda"), Failure("K1", "Managed") };

        var merged = FuzzFailureMerger.Merge(cpu, cuda);

        // mutant: the paths keep their reporting order, or the stage keeps the cpu prefix
        Assert.Equal(2, merged.Count);
        Assert.Equal("cuda_K1", merged[0].Stage);
        Assert.Equal(["Managed", "Native", "Cuda"], Paths(merged[0].Details));
        Assert.Equal("cpu_K2", merged[1].Stage);
        Assert.Equal(["Managed"], Paths(merged[1].Details));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Merge_FailureWithoutKernel_IsKeptAsBatchFailureBeforeTheKernelFailures()
    {
        var cpu = new List<JsonObject> { Failure("K1", "Native"), new() { ["status"] = "harness_error" } };

        var merged = FuzzFailureMerger.Merge(cpu, []);

        // mutant: a failure without a kernel name is dropped or sorted after the kernels
        Assert.Equal(["cpu_batch", "cpu_K1"], [merged[0].Stage, merged[1].Stage]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FuzzRunTotals_CountsPerKnownIssue()
    {
        var totals = new FuzzRunTotals();

        totals.Count(new JsonObject { ["suspectedIssue"] = 1078 });
        totals.Count(new JsonObject { ["suspectedIssue"] = 998 });
        totals.Count(new JsonObject { ["suspectedIssue"] = 1078 });
        totals.Count(new JsonObject { ["status"] = "hang" });
        totals.Count(new JsonObject { ["status"] = "failed" });

        Assert.Equal(5, totals.Failures);
        Assert.Equal(2, totals.SuspectedIssues[1078]);
        Assert.Equal(1, totals.SuspectedIssues[998]);
        Assert.Equal(1, totals.Hangs);
        Assert.Equal(1, totals.OtherFindings);
    }

    private static JsonObject Failure(string kernel, string path) =>
        new() { ["status"] = "failed", ["kernel"] = kernel, ["failingPaths"] = new JsonArray(path) };

    private static JsonObject Passed(string kernel) => new() { ["status"] = "passed", ["kernel"] = kernel };

    private static string[] Paths(JsonObject details)
    {
        var result = new List<string>();
        foreach (var item in details["failingPaths"]?.AsArray() ?? [])
        {
            result.Add(item?.ToString() ?? string.Empty);
        }

        return [.. result];
    }
}
