#nullable enable
using System.Text;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>The Markdown and GitHub writers, the baseline filter and the options that choose among formats.</summary>
[Collection(TestCollections.GlobalStateSequential)]
public class ReportFormatTests
{
    private const string Config = "Config/all-detectors.yml";
    private const string IfChains = "src/LongIfChainCases.cs";

    private static string ConfigPath => TestCaseFiles.PathOf(Config);

    private static (int ExitCode, string Error) RunMain(params string[] args)
    {
        var originalError = Console.Error;
        var originalOut = Console.Out;
        var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Console.SetOut(TextWriter.Null);
            return (Program.Main(args), error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOut);
        }
    }

    private static string Full(ScanWorkspace workspace, string relative) => Path.Combine(workspace.Root, relative);

    private static string Markdown(ScanReport report) => Encoding.UTF8.GetString(MarkdownReportWriter.Write(report));

    private static string Github(ScanReport report) => Encoding.UTF8.GetString(GithubAnnotationWriter.Write(report));

    private static string Baseline(ScanReport report) => Encoding.UTF8.GetString(JsonReportWriter.Write(report));

    // A method per branch count from `from` to `to`, each an if chain that the long-if-chain rule (limit 5) reports.
    private static string IfChainsOfEveryLength(int from, int to, string className = "Chains")
    {
        var source = new StringBuilder("namespace Cases;\npublic static class ").Append(className).Append("\n{\n");
        for (var branches = from; branches <= to; branches++)
        {
            source.Append("    public static int B").Append(branches).Append("(int x)\n    {\n");
            for (var i = 1; i < branches; i++)
            {
                source.Append(i == 1 ? "        if" : "        else if").Append(" (x == ").Append(i).Append(") { return ").Append(i).Append("; }\n");
            }

            source.Append("        else { return 0; }\n    }\n");
        }

        return source.Append("}\n").ToString();
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Markdown_TwoProjects_CountsFindingsByRuleProjectAndFile()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("one/One.csproj", "<Project />");
        workspace.Write("two/Two.csproj", "<Project />");
        workspace.Add("LongIfChainCases.cs", "one/LongIfChainCases.cs");
        workspace.Add("LongIfChainCases.cs", "two/LongIfChainCases.cs");
        workspace.Add("LargeSwitchCases.cs", "two/LargeSwitchCases.cs");
        var report = workspace.Scan(Config, 1, "one/LongIfChainCases.cs", "two/LongIfChainCases.cs", "two/LargeSwitchCases.cs");

        var text = Markdown(report);

        Assert.StartsWith("# Smell scan\n\n" + report.Findings.Length + " findings in 3 files.", text);
        Assert.Contains("| `long-if-chain` | 2 |\n", text);
        Assert.Contains("| `long-string-literal` | 0 |\n", text);
        Assert.Contains("## Findings by project\n\n| Project | Findings |\n| :- | -: |\n| `two` | ", text);
        Assert.Contains("| `one` | 3 |\n", text);
        Assert.Contains("| `one/LongIfChainCases.cs` | 3 |\n", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Markdown_OneRule_ListsTheLargestFirstWithLimitPlaceAndSymbol()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);

        var text = Markdown(workspace.Scan(Config, 1, IfChains));

        Assert.Contains("## `long-if-chain` (branches)\n\nDecompose the conditional.\n\n1 findings, 1 listed, largest first.\n\n", text);
        Assert.Contains(
            "| Value | Limit | Place | Symbol |\n| -: | -: | :- | :- |\n| 7 | 5 | `src/LongIfChainCases.cs:40-46` | `Cases.LongIfChainCases.Seven(int)` |\n",
            text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Markdown_MoreFindingsThanTheLimit_ListsOnlyTheTwentyLargest()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Chains.cs", IfChainsOfEveryLength(6, 30));

        var text = Markdown(workspace.Scan(Config, 1, "src/Chains.cs"));

        // 25 chains of 6 to 30 branches: the 20 largest are 11 to 30
        Assert.Contains("25 findings, 20 listed, largest first.\n", text);
        Assert.Contains("`Cases.Chains.B30(int)`", text);
        Assert.Contains("`Cases.Chains.B11(int)`", text);
        Assert.DoesNotContain("`Cases.Chains.B10(int)`", text);
        Assert.True(text.IndexOf("B30(int)", StringComparison.Ordinal) < text.IndexOf("B29(int)", StringComparison.Ordinal));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Markdown_ExactlyTwentyFindings_ListsAllOfThem()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Chains.cs", IfChainsOfEveryLength(6, 25));

        var text = Markdown(workspace.Scan(Config, 1, "src/Chains.cs"));

        Assert.Contains("20 findings, 20 listed, largest first.\n", text);
        Assert.Contains("`Cases.Chains.B6(int)`", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Markdown_ManyFiles_ListsTheTwentyWithTheMostFindings()
    {
        using var workspace = new ScanWorkspace();
        var files = new List<string>();
        for (var i = 1; i <= 22; i++)
        {
            var path = $"src/F{i:00}.cs";
            workspace.Write(path, IfChainsOfEveryLength(6, 6 + (i % 3), "Chains" + i));
            files.Add(path);
        }

        var text = Markdown(workspace.Scan(Config, 1, [.. files]));

        Assert.Contains("## Files with the most findings (20 of 22)\n", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Markdown_NothingFound_StillWritesTheRuleTableWithZeros()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Empty.cs", "namespace N;\n");

        var text = Markdown(workspace.Scan(Config, 1, "src/Empty.cs"));

        Assert.Contains("0 findings in 1 files.", text);
        Assert.Contains("| `long-if-chain` | 0 |\n", text);
        Assert.DoesNotContain("## `", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Markdown_ParallelismOneAndMany_ProducesTheSameBytes()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        var files = new List<string>();
        foreach (var caseFile in new[] { "LongIfChainCases.cs", "LongStringCases.cs", "LargeSwitchCases.cs" })
        {
            workspace.Add(caseFile, "proj/" + caseFile);
            files.Add("proj/" + caseFile);
        }

        Assert.Equal(Markdown(workspace.Scan(Config, 1, [.. files])), Markdown(workspace.Scan(Config, 8, [.. files])));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Github_Findings_AreWarningsOnTheLinesOfTheSymbolEvenForAnErrorRule()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);

        var text = Github(workspace.Scan(Config, 1, IfChains));

        Assert.Contains(
            "::warning file=src/LongIfChainCases.cs,line=40,endLine=46,title=smell-scan long-if-chain::"
            + "Cases.LongIfChainCases.Seven(int): 7 branches in one if chain (limit 5). Decompose the conditional.\n",
            text);
        Assert.DoesNotContain("::error", text);
        Assert.EndsWith("smell-scan: 3 findings\n", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Github_SpecialCharacters_AreEscapedInPropertiesAndMessage()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);
        var report = workspace.Scan(Config, 1, IfChains);
        var first = report.Findings[0] with
        {
            Suggestion = "100% sure\nsecond line",
            Measurement = report.Findings[0].Measurement with { Path = "src/a,b:c.cs" },
        };

        var text = Github(report with { Findings = [first] });

        Assert.Contains("file=src/a%2Cb%3Ac.cs,", text);
        Assert.Contains("100%25 sure%0Asecond line", text);
        Assert.Equal(2, text.Split('\n').Length - 1);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Github_NoFindings_WritesOnlyTheCount()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Empty.cs", "namespace N;\n");

        Assert.Equal("smell-scan: 0 findings\n", Github(workspace.Scan(Config, 1, "src/Empty.cs")));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_AddedFile_KeepsOnlyItsFindings()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);
        workspace.Add("LargeSwitchCases.cs", "src/LargeSwitchCases.cs");
        var before = workspace.Scan(Config, 1, IfChains);
        var after = workspace.Scan(Config, 1, IfChains, "src/LargeSwitchCases.cs");

        var added = BaselineFilter.KeepNew(after, Baseline(before));

        Assert.NotEmpty(added.Findings);
        Assert.All(added.Findings, static f => Assert.Equal("src/LargeSwitchCases.cs", f.Measurement.Path));
        Assert.Equal(after.Findings.Length - before.Findings.Length, added.Findings.Length);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_FileMovedToAnotherFolder_IsNotNew()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/One/LongIfChainCases.cs");
        workspace.Add("LongIfChainCases.cs", "src/Two/LongIfChainCases.cs");
        var before = workspace.Scan(Config, 1, "src/One/LongIfChainCases.cs");
        var after = workspace.Scan(Config, 1, "src/Two/LongIfChainCases.cs");

        var added = BaselineFilter.KeepNew(after, Baseline(before));

        Assert.Equal(3, after.Findings.Length);
        Assert.Empty(added.Findings);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void KeepNew_ChainGrewByOneBranch_IsNotNewBecauseTheSymbolWasAlreadyReported()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Chains.cs", IfChainsOfEveryLength(6, 7));
        var before = workspace.Scan(Config, 1, "src/Chains.cs");
        workspace.Write("src/Chains.cs", IfChainsOfEveryLength(7, 7));
        var after = workspace.Scan(Config, 1, "src/Chains.cs");

        var added = BaselineFilter.KeepNew(after, Baseline(before));

        Assert.NotEmpty(after.Findings);
        Assert.Empty(added.Findings);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KeepNew_MethodCrossedTheThreshold_IsNew()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Chains.cs", IfChainsOfEveryLength(5, 5));
        var before = workspace.Scan(Config, 1, "src/Chains.cs");
        workspace.Write("src/Chains.cs", IfChainsOfEveryLength(6, 6));
        var after = workspace.Scan(Config, 1, "src/Chains.cs");

        var added = BaselineFilter.KeepNew(after, Baseline(before));

        Assert.DoesNotContain(before.Findings, static f => f.RuleId == "long-if-chain");
        var chain = Assert.Single(added.Findings, static f => f.RuleId == "long-if-chain");
        Assert.Equal("Cases.Chains.B6(int)", chain.Measurement.Symbol);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KeepNew_OnlyOnePartOfAPartialTypeChanged_ReportsTheWholeTypeWhenItCrossesTheLimit()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialAlphaA.cs", "proj/PartialAlphaA.cs");
        var before = workspace.Scan(Config, 1, "proj/PartialAlphaA.cs");
        workspace.Add("PartialAlphaB.cs", "proj/PartialAlphaB.cs");
        var after = workspace.Scan(Config, 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        var added = BaselineFilter.KeepNew(after, Baseline(before));

        Assert.DoesNotContain(before.Findings, static f => f.Measurement.Symbol == "Cases.Split");
        var split = Assert.Single(added.Findings, static f => f.Measurement.Symbol == "Cases.Split" && f.Measurement.Metric == "codeLines");
        Assert.Equal(11, split.Measurement.Value);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"tool\":\"other\",\"files\":{}}")]
    [InlineData("{\"tool\":\"smell-scan\"}")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void KeepNew_TextThatIsNotAReport_Throws(string baseline)
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Empty.cs", "namespace N;\n");
        var report = workspace.Scan(Config, 1, "src/Empty.cs");

        Assert.Throws<UsageException>(() => BaselineFilter.KeepNew(report, baseline));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Parse_FormatList_IsCollectedInOrder()
    {
        var options = CommandLineParser.Parse(["--format", "json,sarif,markdown", "--out", "dir", "--baseline", "b.json"]);

        Assert.Equal([ReportFormat.Json, ReportFormat.Sarif, ReportFormat.Markdown], options.Formats);
        Assert.Equal("b.json", options.Baseline);
        Assert.Equal([ReportFormat.Github], CommandLineParser.Parse(["--format", "github"]).Formats);
    }

    [Theory]
    [InlineData("--format", "json,sarif")]
    [InlineData("--format", "json,github", "--out", "dir")]
    [InlineData("--format", "json,json", "--out", "dir")]
    [InlineData("--format", "json,")]
    [InlineData("--format", "html")]
    [InlineData("--report", "metrics", "--format", "markdown")]
    [InlineData("--report", "metrics", "--baseline", "b.json")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_BadFormatCombination_Throws(string first, string second, string? third = null, string? fourth = null)
    {
        var arguments = new List<string> { first, second };
        if (third is not null)
        {
            arguments.Add(third);
        }

        if (fourth is not null)
        {
            arguments.Add(fourth);
        }

        Assert.Throws<UsageException>(() => CommandLineParser.Parse([.. arguments]));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_SeveralFormats_WritesOneFileEachIntoTheDirectory()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);
        var directory = Path.Combine(workspace.Root, "out", "nested");

        var (exitCode, _) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, Full(workspace, IfChains), "--format", "json,sarif,markdown,text", "--out", directory);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("{", File.ReadAllText(Path.Combine(directory, "smells.json")));
        Assert.Contains("\"version\": \"2.1.0\"", File.ReadAllText(Path.Combine(directory, "smells.sarif")));
        Assert.StartsWith("# Smell scan", File.ReadAllText(Path.Combine(directory, "smells.md")));
        Assert.StartsWith("== src/LongIfChainCases.cs", File.ReadAllText(Path.Combine(directory, "smells.txt")));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_BaselineWithGithubFormat_ReportsOnlyTheFindingsTheBaselineLacks()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);
        var baseline = Path.Combine(workspace.Root, "baseline.json");
        var output = Path.Combine(workspace.Root, "new.txt");
        Assert.Equal(0, RunMain("--root", workspace.Root, "--config", ConfigPath, Full(workspace, IfChains), "--out", baseline).ExitCode);
        workspace.Add("LargeSwitchCases.cs", "src/LargeSwitchCases.cs");

        var (exitCode, _) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, Full(workspace, IfChains), Full(workspace, "src/LargeSwitchCases.cs"),
            "--baseline", baseline, "--format", "github", "--out", output);

        var text = File.ReadAllText(output);
        Assert.Equal(0, exitCode);
        Assert.Contains("::warning file=src/LargeSwitchCases.cs,", text);
        Assert.DoesNotContain("LongIfChainCases", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Main_BaselineThatIsNotAReport_ReturnsTwoWithTheReason()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", IfChains);
        workspace.Write("baseline.json", "[]");

        var (exitCode, error) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, Full(workspace, IfChains), "--baseline", Full(workspace, "baseline.json"));

        Assert.Equal(2, exitCode);
        Assert.Contains("not a JSON report of smell-scan", error);
    }
}
