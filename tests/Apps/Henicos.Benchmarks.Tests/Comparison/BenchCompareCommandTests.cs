using System.Text.Json;
using Henicos.Benchmarks.Comparison;

namespace Henicos.Benchmarks.Tests.Comparison;

// The reports under Comparison/Reports hold two benchmarks of FixtureBenchmark with ten actual workload iterations
// each, plus an overhead and a pilot iteration that the comparison must skip. Parse runs two operations per iteration
// (about 1000 ns per operation), Emit one (about 2000 ns). Base uses the generated job id Job-ABCDEF, the other
// reports Job-QWERTY with the same characteristics, so the two must still match.
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class BenchCompareCommandTests
{
    private const string ParseKey =
        "Henicos.Benchmarks.Fixtures.FixtureBenchmark.Parse @ Job(Toolchain=InProcessEmitToolchain)";

    private const string EmitKey =
        "Henicos.Benchmarks.Fixtures.FixtureBenchmark.Emit @ Job(Toolchain=InProcessEmitToolchain)";

    private const string LowerKey =
        "Henicos.Benchmarks.Fixtures.FixtureBenchmark.Lower @ Job(Toolchain=InProcessEmitToolchain)";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_ClearSlowdown_ReportsRegressionAndExitsOne()
    {
        var (exitCode, output, _) = Run(Reports("Base"), Reports("Slower"));

        Assert.Equal(BenchCompareCommand.ExitRegression, exitCode);
        Assert.Contains("| FixtureBenchmark.Parse (Job-ABCDEF) |", output);
        Assert.Contains("Summary: 2 compared, 1 regression(s), 0 improvement(s), 1 unchanged", output);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_ClearSlowdown_JsonOutHoldsPerOperationSamplesOfActualWorkloadOnly()
    {
        var jsonOut = Path.Combine(Path.GetTempPath(), $"bench-compare-{Guid.NewGuid():N}", "result.json");
        try
        {
            var (exitCode, _, _) = Run(Reports("Base"), Reports("Slower"), "--json-out", jsonOut);

            Assert.Equal(BenchCompareCommand.ExitRegression, exitCode);
            using var document = JsonDocument.Parse(File.ReadAllText(jsonOut));
            var root = document.RootElement;
            Assert.Equal(5.0, root.GetProperty("thresholdPercent").GetDouble());
            Assert.Equal(0.01, root.GetProperty("pValueThreshold").GetDouble());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("regressions").GetInt32());

            var parse = FindComparison(root, ParseKey);
            Assert.Equal("Regression", parse.GetProperty("status").GetString());
            // Ten iterations of two operations: the overhead and pilot iterations are not samples
            Assert.Equal(10, parse.GetProperty("base").GetProperty("n").GetInt32());
            Assert.Equal(1001.0, parse.GetProperty("base").GetProperty("median").GetDouble(), 9);
            Assert.Equal(990.0, parse.GetProperty("base").GetProperty("min").GetDouble(), 9);
            Assert.Equal(30.0, parse.GetProperty("medianDeltaPercent").GetDouble(), 0.5);
            // Every head sample is slower than every base sample: U = 0, exact p = 2 / C(20, 10)
            Assert.Equal(0.0, parse.GetProperty("u").GetDouble());
            Assert.True(parse.GetProperty("pValue").GetDouble() < 1e-4);

            var emit = FindComparison(root, EmitKey);
            Assert.Equal("Unchanged", emit.GetProperty("status").GetString());
            Assert.Equal(1.0, emit.GetProperty("pValue").GetDouble(), 9);
        }
        finally
        {
            var directory = Path.GetDirectoryName(jsonOut);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_SameRunOnBothSides_ExitsZero()
    {
        var (exitCode, output, error) = Run(Reports("Base"), Reports("Base"));

        Assert.Equal(BenchCompareCommand.ExitSuccess, exitCode);
        Assert.Contains("Summary: 2 compared, 0 regression(s), 0 improvement(s), 2 unchanged", output);
        Assert.Equal("", error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_SpeedUp_ReportsImprovementAndExitsZero()
    {
        var (exitCode, output, _) = Run(Reports("Base"), Reports("Faster"));

        Assert.Equal(BenchCompareCommand.ExitSuccess, exitCode);
        Assert.Contains("-30.", output);
        Assert.Contains("Improvement", output);
        Assert.Contains("Summary: 2 compared, 0 regression(s), 1 improvement(s), 1 unchanged", output);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_ThresholdAboveTheSlowdown_ExitsZero()
    {
        var (exitCode, output, _) = Run(Reports("Base"), Reports("Slower"), "--threshold", "35");

        Assert.Equal(BenchCompareCommand.ExitSuccess, exitCode);
        Assert.Contains("0 regression(s)", output);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_SameBenchmarkTwiceOnOneSide_FailsNamingBothFiles()
    {
        var (exitCode, output, error) = Run(Reports("Duplicate"), Reports("Base"));

        Assert.Equal(BenchCompareCommand.ExitInvalidInput, exitCode);
        Assert.Contains($"Benchmark '{ParseKey}' appears twice in base", error);
        Assert.Contains(Path.Combine(Reports("Duplicate"), "First"), error);
        Assert.Contains(Path.Combine(Reports("Duplicate"), "Second"), error);
        Assert.Equal("", output);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_BenchmarkOnOneSideOnly_ReportsItAsUnmatched()
    {
        var (exitCode, output, error) = Run(Reports("Base"), Reports("Unmatched"));

        Assert.Equal(BenchCompareCommand.ExitSuccess, exitCode);
        Assert.Contains($"Only in base (not compared):{Environment.NewLine}  {EmitKey}", output);
        Assert.Contains($"Only in head (not compared):{Environment.NewLine}  {LowerKey}", output);
        Assert.Contains(
            "Summary: 1 compared, 0 regression(s), 0 improvement(s), 1 unchanged, 1 only in base, 1 only in head.",
            output);
        Assert.Equal("", error);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_FullAndCompressedReportOfOneRun_ReadsTheRunOnce()
    {
        var (exitCode, output, error) = Run(Reports("FullAndCompressed"), Reports("Base"));

        Assert.Equal(BenchCompareCommand.ExitSuccess, exitCode);
        Assert.Contains("Summary: 2 compared", output);
        Assert.Equal("", error);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_MissingPath_ExitsTwo()
    {
        var missing = Path.Combine(Reports("Base"), "missing");

        var (exitCode, _, error) = Run(missing, Reports("Base"));

        Assert.Equal(BenchCompareCommand.ExitInvalidInput, exitCode);
        Assert.Contains($"The base path does not exist: {missing}", error);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("--pvalue", "0")]
    [InlineData("--pvalue", "1.5")]
    [InlineData("--threshold", "abc")]
    [InlineData("--unknown", "x")]
    public void Run_InvalidOption_ExitsTwo(string option, string value)
    {
        var (exitCode, _, error) = Run(Reports("Base"), Reports("Base"), option, value);

        Assert.Equal(BenchCompareCommand.ExitInvalidInput, exitCode);
        Assert.Contains(option, error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_Help_PrintsUsageAndExitsZero()
    {
        var (exitCode, output, _) = Run("--help");

        Assert.Equal(BenchCompareCommand.ExitSuccess, exitCode);
        Assert.Contains("Usage: bench-compare <base> <head>", output);
    }

    // scipy.stats.mannwhitneyu(x, y, method="exact") gives U = 8, p = 0.00072528 for these samples (no ties)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MannWhitney_NoTiesTenAndTen_MatchesScipyExactPValue()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7, 8, 11, 16];
        double[] y = [9, 10, 12, 13, 14, 15, 17, 18, 19, 20];

        var (u, pValue) = BenchmarkComparer.MannWhitney(x, y);

        Assert.Equal(8.0, u);
        Assert.Equal(0.00072528, pValue, 8);
    }

    private static string Reports(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Comparison", "Reports", name);

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = BenchCompareCommand.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static JsonElement FindComparison(JsonElement root, string key)
    {
        foreach (var comparison in root.GetProperty("comparisons").EnumerateArray())
        {
            if (comparison.GetProperty("key").GetString() == key)
            {
                return comparison;
            }
        }

        throw new Xunit.Sdk.XunitException($"No comparison for {key}.");
    }
}
