#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Kassyi.GSharp.Tests.Infrastructure.Tests;

/// <summary>
/// Runs <see cref="MemoryBudgetFixtureTests"/> and the final check of this assembly in a child <c>dotnet test</c> with
/// a small budget and a 1 GB allocation, so the real watchdog, the per-test tracking of xUnit and the final collection
/// are exercised together. The same run with a raised budget passes.
/// </summary>
public class MemoryBudgetBreachTests
{
    private const int AllocationMegabytes = 1024;
    private const int SmallBudgetMegabytes = 512;
    private const int RaisedBudgetMegabytes = 8192;
    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(5);

    private const string FinalCheck =
        "Kassyi.GSharp.Tests.Infrastructure.MemoryBudgetHooks.MemoryBudgetFinalCheck.WholeRunPeak_StaysWithinBudget";

    private static readonly string s_fixtureTest =
        $"{typeof(MemoryBudgetFixtureTests).FullName}." +
        nameof(MemoryBudgetFixtureTests.Allocate_PastBudget_TouchesAllBytes);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Run_AboveBudget_FailsWithPeakBudgetAndItsName()
    {
        (int exitCode, string output) = RunFixture(SmallBudgetMegabytes);

        Assert.NotEqual(0, exitCode);
        Assert.Contains($"Failed {s_fixtureTest}", output, StringComparison.Ordinal);
        Assert.Contains($"Failed {FinalCheck}", output, StringComparison.Ordinal);
        Assert.Contains(
            "The test host of Kassyi.GSharp.Tests.Infrastructure.Tests exceeded its memory budget",
            output,
            StringComparison.Ordinal
        );
        Assert.Contains($"{ProcessMemory.PeakMetricName} ", output, StringComparison.Ordinal);
        Assert.Contains($"budget {SmallBudgetMegabytes} MB", output, StringComparison.Ordinal);
        Assert.Contains($"Tests running when the breach was seen: {s_fixtureTest}", output, StringComparison.Ordinal);
        Assert.Contains(
            $"Largest growth of the peak during a test: {s_fixtureTest} +",
            output,
            StringComparison.Ordinal
        );
        // The snapshot names its files, or says why it failed (no network for dotnet dnx, for example)
        Assert.Contains("Heap snapshot", output, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void SameRunWithRaisedBudget_Passes()
    {
        (int exitCode, string output) = RunFixture(RaisedBudgetMegabytes);

        Assert.True(exitCode == 0, output);
        Assert.Contains($"Passed {s_fixtureTest}", output, StringComparison.Ordinal);
        Assert.Contains($"Passed {FinalCheck}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("exceeded its memory budget", output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunFixture(int budgetMegabytes)
    {
        string configuration = typeof(MemoryBudgetBreachTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepositoryPaths.Root,
        };
        foreach (string argument in new[]
        {
            "test",
            RepositoryPaths.Combine(
                "tests",
                "Shared",
                "Kassyi.GSharp.Tests.Infrastructure.Tests",
                "Kassyi.GSharp.Tests.Infrastructure.Tests.csproj"
            ),
            "--no-build",
            "-c", configuration,
            "--filter",
            $"FullyQualifiedName~{nameof(MemoryBudgetFixtureTests)}|FullyQualifiedName~MemoryBudgetFinalCheck",
            "--logger", "console;verbosity=normal",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[MemoryBudget.BudgetVariable] = budgetMegabytes.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment[MemoryBudgetFixtureTests.AllocationVariable] =
            AllocationMegabytes.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment.Remove(MemoryBudget.ReportVariable);
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DiffEngine_Disabled"] = "true";
        // The assertions read the English runner output ("Failed <test>", "Passed <test>")
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        startInfo.Environment["VSLANG"] = "1033";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet test did not start.");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(s_timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet test did not finish in {s_timeout}. Output so far:\n{Snapshot(output)}"
            );
        }

        process.WaitForExit();
        return (process.ExitCode, Snapshot(output));
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }
}
