#nullable enable
using System.Text.Json;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class ScanReportTests
{
    private const string Config = "Config/low-thresholds.yml";

    private static string[] PopulateManyFiles(ScanWorkspace workspace)
    {
        var paths = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            foreach (var (caseFile, folder) in new[]
            {
                ("MethodLengthCases.cs", "src/A"),
                ("NestingDepthCases.cs", "src/B"),
                ("ParameterCountCases.cs", "tests/C"),
                ("PreprocessorCases.cs", "tools/D"),
            })
            {
                var path = $"{folder}/File{i:00}{caseFile}";
                workspace.Add(caseFile, path);
                paths.Add(path);
            }
        }

        return [.. paths];
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_SameFilesWithParallelismOneAndMany_ProducesTheSameBytes()
    {
        using var workspace = new ScanWorkspace();
        var files = PopulateManyFiles(workspace);

        var sequential = JsonReportWriter.Write(workspace.Scan(Config, 1, files));
        var parallel = JsonReportWriter.Write(workspace.Scan(Config, 8, files));
        var reversedInput = new List<string>(files);
        reversedInput.Reverse();
        var reordered = JsonReportWriter.Write(workspace.Scan(Config, 4, [.. reversedInput]));

        Assert.True(sequential.Length > 10_000, "The scan should find many things to make the comparison meaningful.");
        Assert.Equal(sequential, parallel);
        Assert.Equal(sequential, reordered);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_Report_HasFixedKeyOrderNoBomAndEndsWithLineFeed()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("MethodLengthCases.cs", "src/MethodLengthCases.cs");

        var bytes = JsonReportWriter.Write(workspace.Scan(Config, 1, "src/MethodLengthCases.cs"));

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'\n', bytes[^1]);
        using var document = JsonDocument.Parse(bytes);
        var names = new List<string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            names.Add(property.Name);
        }

        // "commit" is present only inside a git checkout, and the temporary workspace is not one.
        Assert.Equal(["tool", "schema", "config", "configHash", "scope", "summary", "files"], names);
        Assert.Equal("smell-scan", document.RootElement.GetProperty("tool").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal("syntax", document.RootElement.GetProperty("scope").GetProperty("tier").GetString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_Finding_CarriesRangeMetricThresholdAndPhysicalLines()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("MethodLengthCases.cs", "src/MethodLengthCases.cs");

        var bytes = JsonReportWriter.Write(workspace.Scan(Config, 1, "src/MethodLengthCases.cs"));

        using var document = JsonDocument.Parse(bytes);
        var findings = document.RootElement.GetProperty("files").GetProperty("src/MethodLengthCases.cs");
        JsonElement? expression = null;
        foreach (var finding in findings.EnumerateArray())
        {
            if (finding.GetProperty("symbol").GetString() == "Cases.MethodLengthCases.ExpressionBody()"
                && finding.GetProperty("rule").GetString() == "long-method")
            {
                expression = finding;
            }
        }

        // ExpressionBody has 3 code lines, which is not above the threshold of 3.
        Assert.Null(expression);

        JsonElement? blank = null;
        foreach (var finding in findings.EnumerateArray())
        {
            if (finding.GetProperty("symbol").GetString() == "Cases.MethodLengthCases.BlankAndCommentLinesAreFree()")
            {
                blank = finding;
            }
        }

        var found = Assert.NotNull(blank);
        Assert.Equal("long-method", found.GetProperty("rule").GetString());
        Assert.Equal("warning", found.GetProperty("severity").GetString());
        Assert.Equal(10, found.GetProperty("startLine").GetInt32());
        Assert.Equal(5, found.GetProperty("startColumn").GetInt32());
        Assert.Equal(19, found.GetProperty("endLine").GetInt32());
        Assert.Equal(6, found.GetProperty("endColumn").GetInt32());
        Assert.Equal("codeLines", found.GetProperty("metric").GetString());
        Assert.Equal(5, found.GetProperty("value").GetInt32());
        Assert.Equal(3, found.GetProperty("threshold").GetInt32());
        Assert.Equal(10, found.GetProperty("physicalLines").GetInt32());
        Assert.Equal("Extract Method.", found.GetProperty("suggestion").GetString());
        Assert.Equal(16, found.GetProperty("fingerprint").GetString()?.Length);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_Summary_CountsEveryActiveRuleEvenWithoutFindingsAndSkipsRulesThatAreOff()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("MethodLengthCases.cs", "src/MethodLengthCases.cs");

        var bytes = JsonReportWriter.Write(workspace.Scan(Config, 1, "src/MethodLengthCases.cs"));

        using var document = JsonDocument.Parse(bytes);
        var byRule = document.RootElement.GetProperty("summary").GetProperty("byRule");
        var rules = new List<string>();
        foreach (var rule in byRule.EnumerateObject())
        {
            rules.Add(rule.Name);
        }

        Assert.Equal(["deep-nesting", "long-method", "long-parameter-list"], rules);
        Assert.Equal(0, byRule.GetProperty("deep-nesting").GetInt32());
        Assert.True(byRule.GetProperty("long-method").GetInt32() > 0);
        var byProject = document.RootElement.GetProperty("summary").GetProperty("byProject");
        Assert.Equal(byRule.GetProperty("long-method").GetInt32() + byRule.GetProperty("long-parameter-list").GetInt32(),
            byProject.GetProperty("src").GetInt32());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Fingerprint_TypeMovedToAnotherFileAndLine_StaysTheSame()
    {
        using var before = new ScanWorkspace();
        before.Add("MovedBefore.cs", "src/One/Mover.cs");
        using var after = new ScanWorkspace();
        after.Add("MovedAfter.cs", "src/Two/Deeper/Moved.cs");

        var beforeFinding = Assert.Single(before.Scan(Config, 1, "src/One/Mover.cs").Findings);
        var afterFinding = Assert.Single(after.Scan(Config, 1, "src/Two/Deeper/Moved.cs").Findings);

        Assert.Equal("long-parameter-list", beforeFinding.RuleId);
        Assert.NotEqual(beforeFinding.Measurement.Path, afterFinding.Measurement.Path);
        Assert.NotEqual(beforeFinding.Measurement.Range.StartLine, afterFinding.Measurement.Range.StartLine);
        Assert.Equal(beforeFinding.Fingerprint, afterFinding.Fingerprint);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Fingerprint_SameSymbolInTwoFiles_GetsDifferentOrdinalsInReportOrder()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("MovedBefore.cs", "src/A/Mover.cs");
        workspace.Add("MovedBefore.cs", "src/B/Mover.cs");

        var report = workspace.Scan(Config, 1, "src/B/Mover.cs", "src/A/Mover.cs");

        Assert.Equal(2, report.Findings.Length);
        Assert.Equal("src/A/Mover.cs", report.Findings[0].Measurement.Path);
        Assert.Equal(report.Findings[0].Measurement.Symbol, report.Findings[1].Measurement.Symbol);
        Assert.NotEqual(report.Findings[0].Fingerprint, report.Findings[1].Fingerprint);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Fingerprint_DifferentRuleOrSymbol_Differs()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("ParameterCountCases.cs", "src/P.cs");

        var report = workspace.Scan(Config, 1, "src/P.cs");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            Assert.True(seen.Add(finding.Fingerprint), $"Duplicate fingerprint for {finding.Measurement.Symbol}.");
        }

        Assert.True(seen.Count > 5);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Run_ConfigWithDifferentLineEndings_HasTheSameHash()
    {
        using var workspace = new ScanWorkspace();
        var text = TestCaseFiles.Read(Config);
        var config = SmellConfigReader.Read(Config, text);
        var runner = new ScanRunner(workspace.Root, config, 1);

        var lf = runner.Run([], Config, text.Replace("\r\n", "\n"));
        var crlf = runner.Run([], Config, text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        var changed = runner.Run([], Config, text.Replace("max_depth: 1", "max_depth: 2"));

        Assert.Equal(lf.ConfigHash, crlf.ConfigHash);
        Assert.NotEqual(lf.ConfigHash, changed.ConfigHash);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Run_NoFiles_ProducesAnEmptyReport()
    {
        using var workspace = new ScanWorkspace();

        var bytes = JsonReportWriter.Write(workspace.Scan(Config, 1));

        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(0, document.RootElement.GetProperty("scope").GetProperty("files").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("files").EnumerateObject());
    }
}
