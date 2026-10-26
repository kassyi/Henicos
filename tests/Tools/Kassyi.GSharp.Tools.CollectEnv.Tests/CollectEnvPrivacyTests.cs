using System.Diagnostics;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.CollectEnv.Tests;

/// <summary>
/// Runs tools/collect-env/collect-env.cs as a process. The report is pasted into public issues, so the redaction
/// of profile paths, user name and host name is pinned here.
/// </summary>
public class CollectEnvPrivacyTests
{
    private const string ProbeName = "GSHARP_PROBE";

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Markdown_RedactsProfilePathUserAndHost()
    {
        (int exitCode, string stdout, _) = RunTool([]);

        Assert.Equal(0, exitCode);
        AssertNoPersonalData(stdout);
        string probeLine = Assert.Single(stdout.Split('\n'), static l => l.Contains(ProbeName, StringComparison.Ordinal));
        Assert.Contains($"{ProbeName}=~", probeLine, StringComparison.Ordinal);
        AssertProbeShape(probeLine);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Json_RedactsProfilePathUserAndHost()
    {
        (int exitCode, string stdout, _) = RunTool(["--json"]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        string probe = document.RootElement.GetProperty("environmentVariables").GetProperty(ProbeName).GetString() ?? string.Empty;
        AssertNoPersonalData(probe);
        Assert.StartsWith("~", probe, StringComparison.Ordinal);
        AssertProbeShape(probe);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void UnknownOption_ExitsWithCode2()
    {
        (int exitCode, string stdout, string stderr) = RunTool(["--no-such-option"]);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("--no-such-option", stderr, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void OutputWithoutPath_ExitsWithCode2()
    {
        (int exitCode, _, _) = RunTool(["--output"]);

        Assert.Equal(2, exitCode);
    }

    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // The tool leaves names shorter than 3 characters alone: replacing "a" or "pc" would damage unrelated words
    // ("GeForce", "Pro"). Such a name is therefore neither expected to be redacted nor checked here.
    private static bool IsRedactableName(string name) => name.Length >= 3;

    private static void AssertNoPersonalData(string text)
    {
        Assert.DoesNotContain(Profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Profile.Replace('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
        if (IsRedactableName(Environment.UserName))
        {
            Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        }

        if (IsRedactableName(Environment.MachineName))
        {
            Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertProbeShape(string probe)
    {
        if (IsRedactableName(Environment.UserName))
        {
            Assert.Contains("<user>", probe, StringComparison.Ordinal);
        }

        if (IsRedactableName(Environment.MachineName))
        {
            Assert.Contains("<host>", probe, StringComparison.Ordinal);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunTool(string[] toolArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryPaths.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(RepositoryPaths.Combine("tools", "collect-env", "collect-env.cs"));
        startInfo.ArgumentList.Add("--");
        foreach (string arg in toolArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Profile path, user name and host name in one value: every one of them must be redacted.
        startInfo.Environment[ProbeName] = $"{Profile}{Path.DirectorySeparatorChar}probe {Environment.UserName} on {Environment.MachineName}";

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("collect-env did not finish in 5 minutes.");
        }

        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
