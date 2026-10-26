#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

public class ProcessRunnerTests
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task Test_ProcessRunner_Echo_Success()
    {
        var runner = ProcessRunner.Default;
        var startInfo = ShellCommand.Echo("Hello World");

        var result = await runner.RunAsync(startInfo);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello World", result.StandardOutput);
        Assert.Empty(result.StandardError.Trim());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ProcessRunner_FileNotFound_ThrowsException()
    {
        var runner = ProcessRunner.Default;
        var startInfo = ShellCommand.Missing();

        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(startInfo));
    }

    [Fact]
    [Category(TestCategory.Stress)]
    public void Test_ProcessRunner_LargeOutput_NoDeadlock()
    {
        // Generate a large output to fill the buffer (usually 4KB on Windows).
        // If we read synchronously, this would deadlock.
        var runner = ProcessRunner.Default;
        var startInfo = ShellCommand.Spam(1000);

        // Run synchronously to prove the synchronous path doesn't deadlock inside.
        var result = runner.Run(startInfo);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out line 1000", result.StandardOutput);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ProcessRunner_Run_LargeStdoutAndStderr_DoesNotDeadlock()
    {
        // Both pipes exceed their buffers, so reading one stream after the other would block the child forever.
        var startInfo = ShellCommand.Spam(3000);

        var result = RunWithTimeout(startInfo);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out line 3000", result.StandardOutput);
        Assert.Contains("err line 3000", result.StandardError);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_ProcessRunner_Run_ProducesSameTextAsRunAsync()
    {
        var startInfo = ShellCommand.Echo("last line");
        var asyncStartInfo = ShellCommand.Echo("last line");

        var sync = ProcessRunner.Default.Run(startInfo);
        var async = await ProcessRunner.Default.RunAsync(asyncStartInfo);

        // The trailing newline is part of the text: callers parse it, so it must survive.
        Assert.Equal(async.StandardOutput, sync.StandardOutput);
        Assert.EndsWith(Environment.NewLine, sync.StandardOutput);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ProcessRunner_Run_CanceledWhileRunning_KillsProcess()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var startInfo = ShellCommand.Sleep(60);
        var stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => ProcessRunner.Default.Run(startInfo, cts.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), "The process was not killed on cancellation.");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ProcessRunner_Run_PreCanceledToken_DoesNotStartProcess()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var startInfo = ShellCommand.Missing();

        // Cancellation wins over the start failure, which proves the process was never started.
        Assert.ThrowsAny<OperationCanceledException>(() => ProcessRunner.Default.Run(startInfo, cts.Token));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ProcessRunner_Run_FileNotFound_ThrowsWin32Exception()
    {
        var startInfo = ShellCommand.Missing();

        Assert.Throws<System.ComponentModel.Win32Exception>(() => ProcessRunner.Default.Run(startInfo));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ProcessRunner_Run_InParallelForEach_AllComplete()
    {
        // The shape used by the compile tasks: many synchronous runs inside Parallel.ForEach.
        var exitCodes = new int[24];

        Parallel.For(0, exitCodes.Length, i =>
        {
            var startInfo = ShellCommand.Echo($"item {i}");
            var result = ProcessRunner.Default.Run(startInfo);
            exitCodes[i] = result.StandardOutput.Contains($"item {i}") ? result.ExitCode : -1;
        });

        Assert.All(exitCodes, code => Assert.Equal(0, code));
    }

    private static ProcessResult RunWithTimeout(ProcessStartInfo startInfo)
    {
        // A deadlock would hang the test host, so the run happens on its own task with a deadline.
        var run = Task.Factory.StartNew(() => ProcessRunner.Default.Run(startInfo), TaskCreationOptions.LongRunning);
        Assert.True(run.Wait(TimeSpan.FromSeconds(60)), "The run did not complete: the pipes deadlocked.");
        return run.Result;
    }
}
