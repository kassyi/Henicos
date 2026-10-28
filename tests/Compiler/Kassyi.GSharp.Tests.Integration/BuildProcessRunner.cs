using System.Diagnostics;
using System.Globalization;

namespace Kassyi.GSharp.Tests.Integration;

/// <summary>
/// Runs <c>dotnet build</c> for the MSBuild integration tests. The wait is generous because the Windows CI runner
/// runs several test assemblies at once; set <c>GSHARP_TEST_BUILD_TIMEOUT_SECONDS</c> to change it.
/// </summary>
internal static class BuildProcessRunner
{
    internal const string TimeoutEnvironmentVariable = "GSHARP_TEST_BUILD_TIMEOUT_SECONDS";

    private const int DefaultTimeoutSeconds = 300;
    private const int OutputTailLength = 4000;

    internal static TimeSpan Timeout
    {
        get
        {
            var text = Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable);
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        }
    }

    /// <summary>Runs <c>dotnet &lt;arguments&gt;</c> and returns its exit code and captured output.</summary>
    /// <exception cref="TimeoutException">The process did not exit in time; the whole process tree has been killed.</exception>
    internal static async Task<BuildResult> RunAsync(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var timeout = Timeout;
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start build process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Kill the MSBuild node and compiler server children too, or they keep the temp directory locked.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var tail = Tail(await stdoutTask) + Tail(await stderrTask);
            throw new TimeoutException(
                $"Build process 'dotnet {arguments}' timed out after {stopwatch.Elapsed.TotalSeconds:F1} s " +
                $"(limit {timeout.TotalSeconds:F0} s, override with {TimeoutEnvironmentVariable}). Output tail:\n{tail}");
        }

        return new BuildResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string Tail(string text) =>
        text.Length <= OutputTailLength ? text : text[^OutputTailLength..];
}

internal readonly record struct BuildResult(int ExitCode, string Stdout, string Stderr);
