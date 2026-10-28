using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tests.Integration;

/// <summary>
/// Runs <c>dotnet build</c> for the MSBuild integration tests. The wait is generous because the Windows CI runner
/// runs several test assemblies at once; set <c>GSHARP_TEST_BUILD_TIMEOUT_SECONDS</c> to change it.
/// </summary>
/// <remarks>
/// Every build writes a binary log. It goes to <c>GSHARP_TEST_BUILD_LOG_DIR</c> when that is set (CI uploads that
/// directory when the job fails), otherwise next to the project directory, where the test's cleanup removes it.
/// The builds run without build servers (<c>--disable-build-servers</c>), so no compiler or MSBuild server outlives the
/// test and keeps its temporary directory locked.
/// </remarks>
internal static class BuildProcessRunner
{
    internal const string TimeoutEnvironmentVariable = "GSHARP_TEST_BUILD_TIMEOUT_SECONDS";
    internal const string LogDirectoryEnvironmentVariable = "GSHARP_TEST_BUILD_LOG_DIR";

    private const int DefaultTimeoutSeconds = 300;

    // Processes that a stalled build may wait on; on Linux the compiler server runs as "dotnet"
    private static readonly string[] s_buildProcessNames = ["dotnet", "MSBuild", "VBCSCompiler"];

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

    /// <summary>
    /// Runs <c>dotnet build &lt;projectFile&gt; -c Debug</c> in <paramref name="workingDirectory"/> with a binary log and
    /// without build servers, and returns its exit code and captured output.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The process did not exit in time; the whole process tree has been killed. The message holds the full output,
    /// the running dotnet / MSBuild / VBCSCompiler processes and the path of the binary log.
    /// </exception>
    internal static async Task<BuildResult> RunBuildAsync(string workingDirectory, string projectFile)
    {
        var binaryLogPath = GetBinaryLogPath(workingDirectory, projectFile);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "build",
                projectFile,
                "-c",
                "Debug",
                "-nr:false",
                "--disable-build-servers",
                $"-bl:{binaryLogPath}"
            }
        };
        var commandLine = "dotnet " + string.Join(' ', startInfo.ArgumentList);

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
            // Take the snapshot before the kill, while the stalled build and whatever it waits on still run
            var processes = DescribeBuildProcesses(process.Id);

            // Kill the MSBuild node and compiler server children too, or they keep the temp directory locked.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"Build process '{commandLine}' timed out after {stopwatch.Elapsed.TotalSeconds:F1} s " +
                $"(limit {timeout.TotalSeconds:F0} s, override with {TimeoutEnvironmentVariable}).\n" +
                $"Binary log: {binaryLogPath}\n" +
                $"Running build processes at the timeout:\n{processes}" +
                $"STDOUT:\n{await stdoutTask}\nSTDERR:\n{await stderrTask}");
        }

        return new BuildResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string GetBinaryLogPath(string workingDirectory, string projectFile)
    {
        var directory = Environment.GetEnvironmentVariable(LogDirectoryEnvironmentVariable);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Path.GetDirectoryName(workingDirectory) ?? workingDirectory;
        }

        Directory.CreateDirectory(directory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(projectFile)}-{stamp}-{Guid.NewGuid():N}.binlog");
    }

    // One line per process: pid, name, start time, CPU time; "*" marks the build that timed out
    private static string DescribeBuildProcesses(int buildProcessId)
    {
        var builder = new StringBuilder();
        foreach (var name in s_buildProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var marker = process.Id == buildProcessId ? "*" : " ";
                    builder.AppendLine(CultureInfo.InvariantCulture,
                        $"{marker} pid {process.Id} {name} started {ReadStartTime(process)} cpu {ReadCpuTime(process)}");
                }
            }
        }

        return builder.Length == 0 ? "(none)\n" : builder.ToString();
    }

    private static string ReadStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().ToString("HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return $"n/a ({ex.GetType().Name})";
        }
    }

    private static string ReadCpuTime(Process process)
    {
        try
        {
            return process.TotalProcessorTime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return $"n/a ({ex.GetType().Name})";
        }
    }
}

internal readonly record struct BuildResult(int ExitCode, string Stdout, string Stderr);
