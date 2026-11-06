#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Runs a test method again in a child <c>dotnet vstest</c> process: for a test whose failure can end the process (an
/// access violation inside the CUDA driver), or that needs a process in which nothing has been launched yet.
/// </summary>
/// <remarks>
/// The test calls <see cref="IsChild"/> first. In the test host it returns <see langword="false"/>, and the test calls
/// <see cref="Run"/>, which starts the child for that one method and fails when the child fails or dies. In the child it
/// returns <see langword="true"/>, and the test does the real work.
/// </remarks>
public static class IsolatedTestProcess
{
    private const string ChildVariable = "GSHARP_TEST_ISOLATED_METHOD";

    /// <summary>Gets whether this process is the child started by <see cref="Run"/> for <paramref name="testMethodName"/>.</summary>
    public static bool IsChild(string testMethodName) =>
        string.Equals(Environment.GetEnvironmentVariable(ChildVariable), testMethodName, StringComparison.Ordinal);

    /// <summary>
    /// Runs <paramref name="testMethodName"/> of <paramref name="testClass"/> in a child process and fails unless the
    /// child exits with code 0 within <paramref name="timeoutMs"/>.
    /// </summary>
    public static void Run(Type testClass, string testMethodName, int timeoutMs = 180_000)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("vstest");
        psi.ArgumentList.Add(testClass.Assembly.Location);
        psi.ArgumentList.Add($"--Tests:{testClass.FullName}.{testMethodName}");
        psi.Environment[ChildVariable] = testMethodName;
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";

        // The summary line checked below is localized otherwise
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        psi.Environment["VSLANG"] = "1033";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet vstest.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"The child process of '{testMethodName}' timed out after {timeoutMs} ms.\nSTDOUT:\n{stdout.Result}\nSTDERR:\n{stderr.Result}");
        }

        process.WaitForExit();
        Assert.True(process.ExitCode == 0,
            $"The child process of '{testMethodName}' failed with exit code {process.ExitCode}.\nSTDOUT:\n{stdout.Result}\nSTDERR:\n{stderr.Result}");

        // vstest exits with 0 when the filter matches nothing or the test is skipped, so one test must have passed
        Assert.Matches(@"Passed:\s+1\b", stdout.Result);
    }
}
