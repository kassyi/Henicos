#nullable enable
using System.Text.Json;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>The options that choose the report, the files to scan and the timing output.</summary>
[Collection(TestCollections.GlobalStateSequential)]
public class ReportOptionsTests
{
    private const string Config = "Config/all-detectors.yml";

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

    private static string[] Arguments(params string?[] values)
    {
        var arguments = new List<string>();
        foreach (var value in values)
        {
            if (value is not null)
            {
                arguments.Add(value);
            }
        }

        return [.. arguments];
    }

    private static string Git(ScanWorkspace workspace, params string[] arguments) =>
        GitRunner.TryRun(workspace.Root, arguments) ?? throw new InvalidOperationException("git " + string.Join(' ', arguments));

    private static void Commit(ScanWorkspace workspace, string message)
    {
        Git(workspace, "add", "-A");
        Git(workspace, "-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "-m", message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Parse_ReportOptions_AreCollected()
    {
        var options = CommandLineParser.Parse(
            ["--format", "sarif", "--changed", "origin/main", "--timing", "--root", "r"]);
        var metrics = CommandLineParser.Parse(["--report", "metrics", "--top", "5"]);

        Assert.Equal(ReportFormat.Sarif, options.Format);
        Assert.Equal("origin/main", options.Changed);
        Assert.True(options.Timing);
        Assert.Equal("r", options.Root);
        Assert.Equal(ReportMode.Metrics, metrics.Report);
        Assert.Equal(5, metrics.Top);
        Assert.Equal(ReportFormat.Text, CommandLineParser.Parse(["--format", "text"]).Format);
        Assert.Equal(ReportFormat.Json, CommandLineParser.Parse(["--format", "json"]).Format);
        Assert.Equal(ReportMode.Findings, CommandLineParser.Parse([]).Report);
    }

    [Theory]
    [InlineData("--report", "everything")]
    [InlineData("--top", "0")]
    [InlineData("--changed")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_BadReportOption_Throws(string first, string? second = null, string? third = null, string? fourth = null)
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse(Arguments(first, second, third, fourth)));
    }

    [Theory]
    [InlineData("--files-from", "a.json", "--changed", "main")]
    [InlineData("--changed", "main", "src/a.cs")]
    [InlineData("--files-from", "a.json", "src/a.cs")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_MoreThanOneSourceOfFiles_Throws(string first, string? second = null, string? third = null, string? fourth = null)
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse(Arguments(first, second, third, fourth)));
    }

    [Theory]
    [InlineData("--report", "metrics", "--format", "json")]
    [InlineData("--report", "metrics", "--format", "sarif")]
    [InlineData("--top", "3")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_MetricsWithAFormatThatIsNotText_OrTopWithoutMetrics_Throws(string first, string? second = null, string? third = null, string? fourth = null)
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse(Arguments(first, second, third, fourth)));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_MetricsWithTextFormat_IsAccepted()
    {
        var options = CommandLineParser.Parse(["--report", "metrics", "--format", "text"]);

        Assert.Equal(ReportMode.Metrics, options.Report);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FromChanged_ListsTheCSharpFilesThatDifferFromTheBaseAndLeavesOutDeletedOnes()
    {
        using var workspace = new ScanWorkspace();
        Git(workspace, "init", "--quiet");
        workspace.Write("a.cs", "class A {}");
        workspace.Write("b.cs", "class B {}");
        workspace.Write("notes.md", "one");
        Commit(workspace, "base");
        workspace.Write("b.cs", "class B { int x; }");
        workspace.Write("dir/c.cs", "class C {}");
        workspace.Write("notes.md", "two");
        File.Delete(Path.Combine(workspace.Root, "a.cs"));
        Commit(workspace, "change");

        var selection = SourceFileSet.FromChanged(workspace.Root, "HEAD~1");

        Assert.Equal(["b.cs", "dir/c.cs"], TestValues.Strings(selection.Files));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FromChanged_BaseThatMovedOnSinceTheBranchStarted_ListsOnlyTheBranchChanges()
    {
        using var workspace = new ScanWorkspace();
        Git(workspace, "init", "--quiet");
        workspace.Write("b.cs", "class B {}");
        workspace.Write("c.cs", "class C {}");
        Commit(workspace, "base");
        var mainBranch = Git(workspace, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        Git(workspace, "branch", "feature");
        workspace.Write("c.cs", "class C { int moved; }");
        Commit(workspace, "base moves on");
        Git(workspace, "checkout", "--quiet", "feature");
        workspace.Write("b.cs", "class B { int mine; }");
        Commit(workspace, "branch change");

        var selection = SourceFileSet.FromChanged(workspace.Root, mainBranch);

        Assert.Equal(["b.cs"], TestValues.Strings(selection.Files));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FromChanged_FilesChangedOnlyAfterTheLastCommit_AreNotListed()
    {
        using var workspace = new ScanWorkspace();
        Git(workspace, "init", "--quiet");
        workspace.Write("a.cs", "class A {}");
        Commit(workspace, "base");
        workspace.Write("a.cs", "class A { int uncommitted; }");

        var selection = SourceFileSet.FromChanged(workspace.Root, "HEAD");

        Assert.Empty(TestValues.Strings(selection.Files));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void FromChanged_UnknownRevision_Throws()
    {
        using var workspace = new ScanWorkspace();
        Git(workspace, "init", "--quiet");
        workspace.Write("a.cs", "class A {}");
        Commit(workspace, "base");

        Assert.Throws<UsageException>(() => SourceFileSet.FromChanged(workspace.Root, "no-such-revision"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_ChangedFiles_AreTheOnlyOnesMeasured()
    {
        using var workspace = new ScanWorkspace();
        Git(workspace, "init", "--quiet");
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");
        workspace.Add("LargeSwitchCases.cs", "src/LargeSwitchCases.cs");
        Commit(workspace, "base");
        workspace.Write("src/LargeSwitchCases.cs", TestCaseFiles.Read("LargeSwitchCases.cs") + "// touched\n");
        Commit(workspace, "change");
        var output = Path.Combine(workspace.Root, "report.txt");

        var (exitCode, _) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, "--changed", "HEAD~1", "--format", "text", "--out", output);

        var text = File.ReadAllText(output);
        Assert.Equal(0, exitCode);
        Assert.Contains("== src/LargeSwitchCases.cs", text);
        Assert.DoesNotContain("LongIfChainCases", text);
        Assert.EndsWith("(1 files measured)\n", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Main_ChangedWithUnknownRevision_ReturnsTwo()
    {
        using var workspace = new ScanWorkspace();
        Git(workspace, "init", "--quiet");
        workspace.Write("a.cs", "class A {}");
        Commit(workspace, "base");

        var (exitCode, error) = RunMain("--root", workspace.Root, "--config", ConfigPath, "--changed", "no-such-revision");

        Assert.Equal(2, exitCode);
        Assert.Contains("no-such-revision", error);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Help_Changed_SaysThatTheWorkingTreeIsMeasured()
    {
        Assert.Contains("read from the working tree", Program.HelpText);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_SarifFormat_WritesSarifToTheOutFile()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");
        var output = Path.Combine(workspace.Root, "report.sarif");

        var (exitCode, _) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, "--format", "sarif", "--out", output,
            Path.Combine(workspace.Root, "src", "LongIfChainCases.cs"));

        using var document = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.Equal(0, exitCode);
        Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
        Assert.NotEqual(0, document.RootElement.GetProperty("runs")[0].GetProperty("results").GetArrayLength());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_MetricsReport_WritesTheLargestMeasurementsAsText()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");
        var output = Path.Combine(workspace.Root, "metrics.txt");

        var (exitCode, _) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, "--report", "metrics", "--top", "1", "--out", output,
            Path.Combine(workspace.Root, "src", "LongIfChainCases.cs"));

        var text = File.ReadAllText(output);
        Assert.Equal(0, exitCode);
        Assert.Contains("== long-if-chain (branches)\n", text);
        Assert.Contains("Seven(int)", text);
        Assert.DoesNotContain("FourBranches", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_Timing_WritesEachPhaseToStandardErrorAndKeepsTheReportUntouched()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");
        var withTiming = Path.Combine(workspace.Root, "a.json");
        var without = Path.Combine(workspace.Root, "b.json");

        var (_, timingError) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, "--timing", "--out", withTiming,
            Path.Combine(workspace.Root, "src", "LongIfChainCases.cs"));
        var (_, plainError) = RunMain(
            "--root", workspace.Root, "--config", ConfigPath, "--out", without,
            Path.Combine(workspace.Root, "src", "LongIfChainCases.cs"));

        Assert.Contains("timing: 1 files", timingError);
        Assert.Contains("timing: parse and measure", timingError);
        Assert.Contains("timing: merge and judge", timingError);
        Assert.Contains("timing: write", timingError);
        Assert.DoesNotContain("timing:", plainError);
        Assert.Equal(File.ReadAllBytes(without), File.ReadAllBytes(withTiming));
    }

    [Theory]
    [InlineData("Config/unknown-detector-parameter.yml", "max_columns")]
    [InlineData("Config/swallowed-with-parameter.yml", "max_catches")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Read_ParameterTheDetectorDoesNotDeclare_IsAConfigurationErrorWithALine(string config, string parameter)
    {
        var exception = Assert.Throws<SmellConfigException>(
            () => SmellConfigReader.Read(config, TestCaseFiles.Read(config)));

        Assert.Contains(parameter, exception.Message);
        Assert.Contains(":", exception.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Read_MemberLimitOfZero_IsAcceptedBecauseItTurnsTheCheckOff()
    {
        var config = SmellConfigReader.Read("Config/repository-rules.yml", TestCaseFiles.Read("Config/repository-rules.yml"));

        Assert.Equal(0, config.Rules[0].Overrides[0].Parameters[TypeSizeDetector.MaxMembers]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Read_LimitBelowTheFloorOfTheStringRule_IsAConfigurationError()
    {
        const string Text = "version: 1\nrules:\n  - id: long-string-literal\n    detector: LongStringLiteral\n    severity: warning\n    parameters:\n      max_columns: 79\n";

        var exception = Assert.Throws<SmellConfigException>(() => SmellConfigReader.Read("floor.yml", Text));

        Assert.Contains("at least 80", exception.Message);
    }
}
