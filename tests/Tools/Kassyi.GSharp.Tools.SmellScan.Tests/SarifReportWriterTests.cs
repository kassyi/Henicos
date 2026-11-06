#nullable enable
using System.Text.Json;
using VerifyXunit;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class SarifReportWriterTests
{
    private const string Config = "Config/all-detectors.yml";
    private const string Fingerprint = "smellScan/v1";

    private static string[] Populate(ScanWorkspace workspace)
    {
        workspace.Write("proj/P.csproj", "<Project />");
        var files = new List<string>();
        foreach (var caseFile in new[]
        {
            "PartialAlphaA.cs",
            "PartialAlphaB.cs",
            "LongIfChainCases.cs",
            "LongStringCases.cs",
            "LargeSwitchCases.cs",
            "SwallowedSmall.cs",
        })
        {
            workspace.Add(caseFile, "proj/" + caseFile);
            files.Add("proj/" + caseFile);
        }

        return [.. files];
    }

    private static JsonElement Run(byte[] sarif)
    {
        using var document = JsonDocument.Parse(sarif);
        return document.RootElement.GetProperty("runs")[0].Clone();
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public Task Write_RepresentativeRun_MatchesTheSnapshot()
    {
        using var workspace = new ScanWorkspace();
        var files = Populate(workspace);

        var sarif = SarifReportWriter.Write(workspace.Scan(Config, 1, files));

        return Verifier.Verify(System.Text.Encoding.UTF8.GetString(sarif), "json");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_Report_DeclaresSarif21AndRulesInIdOrderWithTheirParameters()
    {
        using var workspace = new ScanWorkspace();
        var files = Populate(workspace);

        var bytes = SarifReportWriter.Write(workspace.Scan(Config, 1, files));

        using var document = JsonDocument.Parse(bytes);
        Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
        Assert.Contains("sarif-2.1.0", document.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("utf16CodeUnits", Run(bytes).GetProperty("columnKind").GetString());
        var driver = Run(bytes).GetProperty("tool").GetProperty("driver");
        var ids = new List<string>();
        foreach (var rule in driver.GetProperty("rules").EnumerateArray())
        {
            ids.Add(rule.GetProperty("id").GetString() ?? string.Empty);
        }

        Assert.Equal(["large-switch", "large-type", "long-if-chain", "long-string-literal", "swallowed-exception"], ids);
        var typeRule = driver.GetProperty("rules")[1];
        Assert.Equal("TypeSize", typeRule.GetProperty("name").GetString());
        Assert.Equal(8, typeRule.GetProperty("properties").GetProperty("parameters").GetProperty("max_code_lines").GetInt32());
        Assert.Equal("warning", typeRule.GetProperty("defaultConfiguration").GetProperty("level").GetString());
        Assert.Equal(
            "docs/long-lines.md",
            driver.GetProperty("rules")[3].GetProperty("properties").GetProperty("guidance").GetString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_Result_HasLevelRuleIndexLocationFingerprintAndRelatedParts()
    {
        using var workspace = new ScanWorkspace();
        var files = Populate(workspace);

        var run = Run(SarifReportWriter.Write(workspace.Scan(Config, 1, files)));

        JsonElement? split = null;
        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            var logical = result.GetProperty("locations")[0].GetProperty("logicalLocations")[0];
            if (logical.GetProperty("fullyQualifiedName").GetString() == "Cases.Split"
                && result.GetProperty("properties").GetProperty("metric").GetString() == "codeLines")
            {
                split = result;
            }
        }

        var found = Assert.NotNull(split);
        Assert.Equal("large-type", found.GetProperty("ruleId").GetString());
        Assert.Equal(1, found.GetProperty("ruleIndex").GetInt32());
        Assert.Equal("warning", found.GetProperty("level").GetString());
        var location = found.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("proj/PartialAlphaA.cs", location.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal("SRCROOT", location.GetProperty("artifactLocation").GetProperty("uriBaseId").GetString());
        var region = location.GetProperty("region");
        Assert.Equal(3, region.GetProperty("startLine").GetInt32());
        Assert.Equal(1, region.GetProperty("startColumn").GetInt32());
        Assert.Equal(8, region.GetProperty("endLine").GetInt32());
        Assert.Equal(2, region.GetProperty("endColumn").GetInt32());
        Assert.Equal(16, found.GetProperty("partialFingerprints").GetProperty(Fingerprint).GetString()?.Length);
        var related = Assert.Single(found.GetProperty("relatedLocations").EnumerateArray());
        Assert.Equal(1, related.GetProperty("id").GetInt32());
        Assert.Equal(
            "proj/PartialAlphaB.cs",
            related.GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(11, found.GetProperty("properties").GetProperty("value").GetInt32());
        Assert.Equal(8, found.GetProperty("properties").GetProperty("threshold").GetInt32());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Write_Severities_MapToNoteWarningAndError()
    {
        using var workspace = new ScanWorkspace();
        var files = Populate(workspace);

        var run = Run(SarifReportWriter.Write(workspace.Scan(Config, 1, files)));

        var levels = new Dictionary<string, string>();
        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            levels[result.GetProperty("ruleId").GetString() ?? string.Empty] = result.GetProperty("level").GetString() ?? string.Empty;
        }

        Assert.Equal("note", levels["long-string-literal"]);
        Assert.Equal("error", levels["long-if-chain"]);
        Assert.Equal("warning", levels["swallowed-exception"]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Write_PathWithASpace_IsEscapedPerSegment()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj dir/P.csproj", "<Project />");
        workspace.Add("LongStringCases.cs", "proj dir/long strings.cs");

        var run = Run(SarifReportWriter.Write(workspace.Scan(Config, 1, "proj dir/long strings.cs")));

        var first = run.GetProperty("results")[0].GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("proj%20dir/long%20strings.cs", first.GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Write_SameFilesWithParallelismOneAndMany_ProducesTheSameBytes()
    {
        using var workspace = new ScanWorkspace();
        var files = Populate(workspace);

        var sequential = SarifReportWriter.Write(workspace.Scan(Config, 1, files));
        var parallel = SarifReportWriter.Write(workspace.Scan(Config, 8, files));

        Assert.True(sequential.Length > 5_000);
        Assert.Equal(sequential, parallel);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_LinesShiftedByEdits_KeepEveryFingerprintButNotTheLines()
    {
        using var original = new ScanWorkspace();
        var files = Populate(original);
        using var shifted = new ScanWorkspace();
        shifted.Write("proj/P.csproj", "<Project />");
        foreach (var file in files)
        {
            // Three blank lines above the code move every finding down without changing the code
            shifted.Write(file, "\n\n\n" + File.ReadAllText(Path.Combine(original.Root, file)));
        }

        var before = original.Scan(Config, 1, files);
        var after = shifted.Scan(Config, 1, files);

        Assert.True(before.Findings.Length > 20);
        Assert.Equal(before.Findings.Length, after.Findings.Length);
        for (var i = 0; i < before.Findings.Length; i++)
        {
            Assert.Equal(before.Findings[i].Fingerprint, after.Findings[i].Fingerprint);
            Assert.Equal(before.Findings[i].Measurement.Range.StartLine + 3, after.Findings[i].Measurement.Range.StartLine);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_EveryFindingOfARun_HasItsOwnFingerprint()
    {
        using var workspace = new ScanWorkspace();
        var files = Populate(workspace);

        var report = workspace.Scan(Config, 1, files);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            Assert.True(seen.Add(finding.Fingerprint), $"{finding.RuleId} {finding.Measurement.Symbol} repeats a fingerprint");
        }
    }
}
