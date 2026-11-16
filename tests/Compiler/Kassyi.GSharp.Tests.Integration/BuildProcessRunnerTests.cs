#nullable enable
using System.Diagnostics;
using FluentAssertions;

namespace Kassyi.GSharp.Tests.Integration;

[Category(TestCategory.Compiler)]
public sealed class BuildProcessRunnerTests : IDisposable
{
    private readonly List<int> _spawnedPids = [];
    private readonly List<string> _pidFiles = [];

    public void Dispose()
    {
        CollectSpawnedPids();
        foreach (var pid in _spawnedPids)
        {
            KillProcessIfRunning(pid);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RunProcessAsync_ChildExitsWhileGrandchildHoldsOutputPipes_ThrowsTimeoutWithPartialOutputAndMetadata()
    {
        var startInfo = CreateGrandchildLauncherStartInfo(
            childWaitMilliseconds: 0,
            stdoutLine: "child-stdout-before-exit",
            stderrLine: "child-stderr-before-exit"
        );
        const string BinaryLogPath = "/tmp/test-normal-exit.binlog";

        using var guardCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runnerTask = BuildProcessRunner.RunProcessAsync(
            startInfo,
            commandLine: "test-build normal-exit",
            binaryLogPath: BinaryLogPath,
            timeout: TimeSpan.FromSeconds(5),
            outputDrainTimeout: TimeSpan.FromMilliseconds(300)
        );

        var completed = await Task.WhenAny(runnerTask, Task.Delay(Timeout.Infinite, guardCts.Token));
        CollectSpawnedPids();
        Assert.True(
            ReferenceEquals(runnerTask, completed),
            "RunProcessAsync hung waiting for stdout/stderr EOF after child exit."
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () => await runnerTask);

        exception.Message.Should().Contain("test-build normal-exit");
        exception.Message.Should().Contain("reading its output did not finish");
        exception.Message.Should().Contain(BinaryLogPath);
        exception.Message.Should().Contain("Running build processes:");
        exception.Message.Should().Contain("child-stdout-before-exit");
        exception.Message.Should().Contain("child-stderr-before-exit");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RunProcessAsync_ChildTimesOutAndSurvivingGrandchildHoldsOutputPipes_ThrowsTimeoutWithPartialOutput()
    {
        var startInfo = CreateGrandchildLauncherStartInfo(
            childWaitMilliseconds: 60_000,
            stdoutLine: "child-stdout-before-timeout",
            stderrLine: "child-stderr-before-timeout"
        );
        const string BinaryLogPath = "/tmp/test-timeout-path.binlog";

        using var guardCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runnerTask = BuildProcessRunner.RunProcessAsync(
            startInfo,
            commandLine: "test-build timeout-path",
            binaryLogPath: BinaryLogPath,
            timeout: TimeSpan.FromMilliseconds(500),
            outputDrainTimeout: TimeSpan.FromMilliseconds(300)
        );

        var completed = await Task.WhenAny(runnerTask, Task.Delay(Timeout.Infinite, guardCts.Token));
        CollectSpawnedPids();
        Assert.True(
            ReferenceEquals(runnerTask, completed),
            "RunProcessAsync hung waiting for stdout/stderr EOF after process kill."
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () => await runnerTask);

        exception.Message.Should().Contain("test-build timeout-path");
        exception.Message.Should().Contain("timed out after");
        exception.Message.Should().Contain(BinaryLogPath);
        exception.Message.Should().Contain("Running build processes at the timeout:");
        exception.Message.Should().Contain("child-stdout-before-timeout");
        exception.Message.Should().Contain("child-stderr-before-timeout");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task RunProcessAsync_ChildExitsAndClosesOutputPipes_ReturnsExitCodeAndCompleteOutput()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-e",
                "console.log('line-out-1'); console.log('line-out-2'); console.error('line-err-1'); process.exit(7);"
            }
        };

        var result = await BuildProcessRunner.RunProcessAsync(
            startInfo,
            commandLine: "test-build clean-exit",
            binaryLogPath: "/tmp/test-clean-exit.binlog",
            timeout: TimeSpan.FromSeconds(10),
            outputDrainTimeout: TimeSpan.FromSeconds(5)
        );

        result.ExitCode.Should().Be(7);
        result.Stdout.Should().Contain("line-out-1");
        result.Stdout.Should().Contain("line-out-2");
        result.Stderr.Should().Contain("line-err-1");
    }

    private ProcessStartInfo CreateGrandchildLauncherStartInfo(
        int childWaitMilliseconds,
        string stdoutLine,
        string stderrLine)
    {
        // Spawn the sleeping grandchild through an intermediate node process that exits right away, so that
        // process.Kill(entireProcessTree: true) on the outer child cannot walk the dead intermediate process to reach
        // the grandchild, while the grandchild still inherits the redirected stdout and stderr file descriptors (1, 2).
        var pidFile = Path.Combine(Path.GetTempPath(), $"gsharp-grandchild-{Guid.NewGuid():N}.pid");
        var innerScript =
            "const { spawn } = require('child_process');" +
            "const fs = require('fs');" +
            "const g = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 600000)'], " +
            "{ detached: true, stdio: ['ignore', 1, 2] });" +
            "g.unref();" +
            "fs.writeFileSync(process.argv[1], String(g.pid));";

        var outerScript =
            "const { spawn } = require('child_process');" +
            "const pidFile = process.argv[1];" +
            "const innerScript = process.argv[2];" +
            "const stdoutLine = process.argv[3];" +
            "const stderrLine = process.argv[4];" +
            $"const waitMs = {childWaitMilliseconds};" +
            "const mid = spawn(process.execPath, ['-e', innerScript, pidFile], { stdio: ['ignore', 1, 2] });" +
            "mid.on('exit', () => {" +
            "  process.stdout.write(stdoutLine + '\\n');" +
            "  process.stderr.write(stderrLine + '\\n', () => {" +
            "    if (waitMs > 0) setTimeout(() => {}, waitMs);" +
            "  });" +
            "});";

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-e",
                outerScript,
                pidFile,
                innerScript,
                stdoutLine,
                stderrLine
            }
        };

        _pidFiles.Add(pidFile);
        return startInfo;
    }

    private void CollectSpawnedPids()
    {
        foreach (var pidFile in _pidFiles)
        {
            TryRecordPidFromFile(pidFile);
        }
    }

    private void TryRecordPidFromFile(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile))
            {
                return;
            }

            var text = File.ReadAllText(pidFile).AsSpan().Trim();
            if (int.TryParse(text, out var pid))
            {
                _spawnedPids.Add(pid);
            }

            File.Delete(pidFile);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Failed to read or delete PID file '{pidFile}': {ex.Message}");
        }
    }

    private static void KillProcessIfRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5_000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"Process {pid} already exited: {ex.Message}");
        }
    }
}
