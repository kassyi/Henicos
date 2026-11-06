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
        bool parsed = RunCommand.TryParseOptions(["--repo-root", s_repoRoot], out var options, out bool showHelp, out _);

        // mutant: the default profile, minutes or batch size changes
        Assert.True(parsed);
        Assert.False(showHelp);
        Assert.NotNull(options);
        Assert.Equal("C", options.Profile);
        Assert.Equal(5, options.Minutes);
        Assert.Null(options.Batches);
        Assert.Equal(64, options.BatchSize);
        Assert.Equal(1UL, options.FuzzSeed);
        Assert.Equal(Path.Combine(s_repoRoot, "tmp", "differential-fuzz"), options.WorkDir);
        Assert.Equal(Path.Combine(s_repoRoot, "tmp", "differential-fuzz", "Generated"), options.OutDir);
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
                "--expect-change", "--work-dir", "w", "--out-dir=o", "--no-reduce", "--no-gpu-lock", "--repo-root", s_repoRoot,
            ],
            out var options,
            out _,
            out string? error);

        // mutant: a spelling is not accepted, a repeated --allow-issue is kept twice, or a relative directory ignores --repo-root
        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal("G", options.Profile);
        Assert.Equal(0.5, options.Minutes);
        Assert.Equal(3, options.Batches);
        Assert.Equal(8, options.BatchSize);
        Assert.Equal(0x2AUL, options.FuzzSeed);
        Assert.Equal(4, options.InputSeed);
        Assert.Equal([1101, 984], options.AllowedIssues);
        Assert.Equal("coalesce-assign-value", options.SwarmFocus);
        Assert.True(options.Metamorphic);
        Assert.Equal("origin/main", options.CompareBase);
        Assert.True(options.ExpectChange);
        Assert.True(options.NoReduce);
        Assert.True(options.NoGpuLock);
        Assert.Equal(Path.Combine(s_repoRoot, "w"), options.WorkDir);
        Assert.Equal(Path.Combine(s_repoRoot, "o"), options.OutDir);
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

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ParseJsonLines_MixedConsoleOutput_KeepsOnlyJsonObjects()
    {
        var events = FuzzRunProcess.ParseJsonLines("building...\r\n{\"status\":\"failed\",\"kernel\":\"K1\"}\n{not json}\n[1,2]\n  {\"status\":\"ok\"}  \n");

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
