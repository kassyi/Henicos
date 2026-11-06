#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>The Node script that runs the tool: arguments go through and the exit code of the tool comes back.</summary>
[Collection(TestCollections.GlobalStateSequential)]
public class FacadeScriptTests
{
    private static (int ExitCode, string Output, string Error) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(RepositoryPaths.Combine("scripts", "human", "smell-scan.js"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("node did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("smell-scan.js did not finish.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Help_PrintsTheUsageOfTheToolAndExitsZero()
    {
        var (exitCode, output, _) = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("node scripts/human/smell-scan.js [options] [paths...]", output);
        Assert.Contains("--baseline <json>", output);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void NoBuild_IsTakenByTheScriptAndNotPassedToTheTool()
    {
        // The tool was built by the tests above or an earlier run; --no-build would make the tool reject an option it does not know
        var (exitCode, output, _) = Run("--help");
        Assert.Equal(0, exitCode);

        var (noBuildExitCode, noBuildOutput, error) = Run("--no-build", "--help");

        Assert.Equal(0, noBuildExitCode);
        Assert.DoesNotContain("Unknown option", error);
        Assert.Equal(output, noBuildOutput);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void UnknownOption_PassesTheToolsExitCodeTwoAndMessageThrough()
    {
        var (exitCode, _, error) = Run("--no-such-option");

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown option '--no-such-option'.", error);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_FindingsInTheFiles_StillExitZeroAndPrintTheReport()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");

        var (exitCode, output, _) = Run(
            "--root", workspace.Root,
            "--config", TestCaseFiles.PathOf("Config/all-detectors.yml"),
            Path.Combine(workspace.Root, "src", "LongIfChainCases.cs"),
            "--format", "text");

        Assert.Equal(0, exitCode);
        Assert.Contains("long-if-chain  Cases.LongIfChainCases.Seven(int)", output);
    }
}
