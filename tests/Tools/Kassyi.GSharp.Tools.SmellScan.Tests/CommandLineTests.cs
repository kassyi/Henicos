#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

[Collection(TestCollections.GlobalStateSequential)]
public class CommandLineTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Parse_OptionsAndPaths_AreCollected()
    {
        var options = CommandLineParser.Parse(
            ["--config", "c.yml", "--parallelism", "3", "--out", "o.json", "--format", "json", "src", "tools/x.cs"]);

        Assert.Equal("c.yml", options.Config);
        Assert.Equal(3, options.Parallelism);
        Assert.Equal("o.json", options.Out);
        Assert.Equal(["src", "tools/x.cs"], options.Paths);
        Assert.False(options.Help);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--config")]
    [InlineData("--parallelism")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_UnknownOptionOrMissingValue_Throws(string argument)
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse([argument]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_ParallelismThatIsNotPositive_Throws(string value)
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse(["--parallelism", value]));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_UnknownFormat_Throws()
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse(["--format", "xml"]));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_Help_ReturnsZero()
    {
        var original = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);

            Assert.Equal(0, Program.Main(["--help"]));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Main_UnknownOption_ReturnsTwo()
    {
        var original = Console.Error;
        try
        {
            Console.SetError(TextWriter.Null);

            Assert.Equal(2, Program.Main(["--bogus"]));
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Main_ConfigWithAnError_ReturnsTwoAndNamesTheLine()
    {
        using var workspace = new ScanWorkspace();
        var original = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetError(captured);

            var exitCode = Program.Main(
                ["--root", workspace.Root, "--config", TestCaseFiles.PathOf("Config/bad-severity.yml")]);

            Assert.Equal(2, exitCode);
            Assert.Contains("bad-severity.yml:5:", captured.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Main_MissingConfigFile_ReturnsTwo()
    {
        using var workspace = new ScanWorkspace();
        var original = Console.Error;
        try
        {
            Console.SetError(TextWriter.Null);

            var exitCode = Program.Main(
                ["--root", workspace.Root, "--config", Path.Combine(workspace.Root, "none.yml")]);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Main_UnreadableFileList_ReturnsOneAsAnInternalError()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("files.json", "not json");
        var original = Console.Error;
        try
        {
            Console.SetError(TextWriter.Null);

            var exitCode = Program.Main(
            [
                "--root", workspace.Root,
                "--config", TestCaseFiles.PathOf("Config/low-thresholds.yml"),
                "--files-from", Path.Combine(workspace.Root, "files.json"),
            ]);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Main_FilesFromList_WritesTheReportAndReturnsZeroEvenWithFindings()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("MethodLengthCases.cs", "src/MethodLengthCases.cs");
        workspace.Write("files.json", """["src/MethodLengthCases.cs", "src/gone.cs"]""");
        var output = Path.Combine(workspace.Root, "report.json");
        var original = Console.Error;
        try
        {
            Console.SetError(TextWriter.Null);

            var exitCode = Program.Main(
            [
                "--root", workspace.Root,
                "--config", TestCaseFiles.PathOf("Config/low-thresholds.yml"),
                "--files-from", Path.Combine(workspace.Root, "files.json"),
                "--out", output,
                "--parallelism", "2",
            ]);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"long-method\"", File.ReadAllText(output));
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
