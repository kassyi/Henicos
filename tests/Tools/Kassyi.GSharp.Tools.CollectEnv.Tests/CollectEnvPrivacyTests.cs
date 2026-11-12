using System.Text.Json;

namespace Kassyi.GSharp.Tools.CollectEnv.Tests;

/// <summary>
/// Runs tools/collect-env/collect-env.cs as a process. The report is pasted into public issues, so the redaction
/// of profile paths, user name and host name is pinned here.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
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
#pragma warning disable RS0030 // The Markdown report of the tool is line-oriented text for people; the probe is one of its lines
        string probeLine = Assert.Single(stdout.Split('\n'), static l => l.Contains(ProbeName, StringComparison.Ordinal));
#pragma warning restore RS0030
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
#pragma warning disable RS0030 // The OS profile path with '/' separators, the spelling the tool may print it in, must not appear either
        Assert.DoesNotContain(Profile.Replace('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
#pragma warning restore RS0030
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

    private static (int ExitCode, string Stdout, string Stderr) RunTool(string[] toolArgs) =>
        CollectEnvToolRunner.Run(
            toolArgs,
            static environment =>
            {
                // Profile path, user name and host name in one value: every one of them must be redacted.
                environment[ProbeName] =
                    $"{Profile}{Path.DirectorySeparatorChar}probe {Environment.UserName} on {Environment.MachineName}";
            });
}
