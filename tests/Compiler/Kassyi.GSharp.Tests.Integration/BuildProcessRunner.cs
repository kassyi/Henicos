#nullable enable
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

    // A short grace period for the redirected stdout and stderr pipes to reach end of file after the build process
    // exits or is killed. When a surviving grandchild (such as a stalled task host or compiler server) inherits the
    // write ends of those pipes, EOF never arrives; bounding the drain prevents the test from hanging without a limit.
    private static readonly TimeSpan s_defaultOutputDrainTimeout = TimeSpan.FromSeconds(30);

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
    /// <param name="workingDirectory">The directory the build runs in.</param>
    /// <param name="projectFile">The project to build, relative to <paramref name="workingDirectory"/>.</param>
    /// <param name="environment">Environment variables set (or, with a <see langword="null"/> value, removed) for the
    /// build only.</param>
    /// <param name="extraArguments">Further arguments of <c>dotnet build</c>, such as targets or properties.</param>
    /// <exception cref="TimeoutException">
    /// The process did not exit in time, or its output pipes did not close after it exited; on a process timeout the
    /// whole process tree has been killed. The message holds the output collected so far, the running dotnet / MSBuild
    /// / VBCSCompiler processes and the path of the binary log.
    /// </exception>
    internal static async Task<BuildResult> RunBuildAsync(
        string workingDirectory,
        string projectFile,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyList<string>? extraArguments = null)
    {
        PinRepositorySdk(workingDirectory);
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
        foreach (var argument in extraArguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            startInfo.Environment[name] = value;
        }

        var commandLine = "dotnet " + string.Join(' ', startInfo.ArgumentList);
        return await RunProcessAsync(startInfo, commandLine, binaryLogPath, Timeout, s_defaultOutputDrainTimeout);
    }

    internal static async Task<BuildResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string commandLine,
        string binaryLogPath,
        TimeSpan timeout,
        TimeSpan outputDrainTimeout)
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start build process.");
        var stdoutBuffer = new OutputBuffer();
        var stderrBuffer = new OutputBuffer();
        var stdoutTask = ReadLinesAsync(process.StandardOutput, stdoutBuffer);
        var stderrTask = ReadLinesAsync(process.StandardError, stderrBuffer);

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
            await DrainOutputAsync(stdoutTask, stderrTask, outputDrainTimeout);
            throw new TimeoutException(
                $"Build process '{commandLine}' timed out after {stopwatch.Elapsed.TotalSeconds:F1} s " +
                $"(limit {timeout.TotalSeconds:F0} s, override with {TimeoutEnvironmentVariable}).\n" +
                $"Binary log: {binaryLogPath}\n" +
                $"Running build processes at the timeout:\n{processes}" +
                $"STDOUT:\n{stdoutBuffer.Snapshot()}\nSTDERR:\n{stderrBuffer.Snapshot()}");
        }

        if (!await DrainOutputAsync(stdoutTask, stderrTask, outputDrainTimeout))
        {
            var processes = DescribeBuildProcesses(process.Id);
            throw new TimeoutException(
                $"Build process '{commandLine}' exited with code {process.ExitCode}, " +
                $"but reading its output did not finish within {outputDrainTimeout.TotalSeconds:F1} s " +
                $"(elapsed {stopwatch.Elapsed.TotalSeconds:F1} s).\n" +
                $"Binary log: {binaryLogPath}\n" +
                $"Running build processes:\n{processes}" +
                $"STDOUT:\n{stdoutBuffer.Snapshot()}\nSTDERR:\n{stderrBuffer.Snapshot()}");
        }

        return new BuildResult(process.ExitCode, stdoutBuffer.Snapshot(), stderrBuffer.Snapshot());
    }

    private static async Task ReadLinesAsync(StreamReader reader, OutputBuffer buffer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            buffer.AppendLine(line);
        }
    }

    private static async Task<bool> DrainOutputAsync(Task stdoutTask, Task stderrTask, TimeSpan timeout)
    {
        var bothTasks = Task.WhenAll(stdoutTask, stderrTask);
        using var cts = new CancellationTokenSource();
        var completed = await Task.WhenAny(bothTasks, Task.Delay(timeout, cts.Token));
        if (!ReferenceEquals(completed, bothTasks))
        {
            return false;
        }

        await cts.CancelAsync();
        await bothTasks;
        return true;
    }

    // The build runs in a temporary directory, which the repository's global.json does not reach. Without a copy the
    // child dotnet selects the newest installed SDK while it inherits the MSBuild environment of the test host's SDK,
    // and the task host of the G# tasks cannot start (MSB4216) on a machine with a newer SDK than the pinned one.
    private static void PinRepositorySdk(string workingDirectory)
    {
        var globalJson = RepositoryPaths.Combine("global.json");
        if (File.Exists(globalJson))
        {
            File.Copy(globalJson, Path.Combine(workingDirectory, "global.json"), overwrite: true);
        }
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

    private sealed class OutputBuffer
    {
        private readonly Lock _sync = new();
        private readonly StringBuilder _builder = new();

        public void AppendLine(string line)
        {
            lock (_sync)
            {
                _builder.AppendLine(line);
            }
        }

        public string Snapshot()
        {
            lock (_sync)
            {
                return _builder.ToString();
            }
        }
    }
}

internal readonly record struct BuildResult(int ExitCode, string Stdout, string Stderr);
