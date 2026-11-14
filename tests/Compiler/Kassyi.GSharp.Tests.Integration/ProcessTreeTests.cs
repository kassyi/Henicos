#nullable enable
using System.Diagnostics;
using System.Text;
using Kassyi.GSharp.Compiler.Tasks.Helpers;

namespace Kassyi.GSharp.Tests.Integration;

[Category(TestCategory.Compiler)]
public sealed class ProcessTreeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "gsharp-process-tree-" + Guid.NewGuid().ToString("N"));

    public ProcessTreeTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Kill_AfterTheProcessWasDisposed_DoesNotThrow()
    {
        // A cancellation can arrive right after a run ended and its Process was disposed; the callback must not throw
        // out of CancellationTokenSource.Cancel().
        using var process = new Process { StartInfo = QuickExit() };
        using var tree = ProcessTree.Start(process);
        process.WaitForExit();
        process.Dispose();

        var failure = Record.Exception(tree.Kill);

        Assert.Null(failure);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Cancel_AfterTheProcessWasDisposed_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        using var process = new Process { StartInfo = QuickExit() };
        using var tree = ProcessTree.Start(process);
        using var registration = cts.Token.Register(
            static state =>
            {
                if (state is ProcessTree started)
                {
                    started.Kill();
                }
            },
            tree
        );
        process.WaitForExit();
        process.Dispose();

        var failure = Record.Exception(cts.Cancel);

        Assert.Null(failure);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Kill_TerminationOfTheJobFails_WalkKillsTheMembersOfTheJob()
    {
        // The grandchild is in the job; a walk that skipped the members of a job it was told had been terminated would
        // leave it running.
        Skip.IfNot(OperatingSystem.IsWindows(), "Job objects are Windows only.");
        using var probe = Kassyi.GSharp.Tests.Shared.Helpers.DescendantProcessProbe.Create();
        var startInfo = probe.WaitingShell(60);
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        using var process = new Process { StartInfo = startInfo };
        using var tree = ProcessTree.Start(process, static _ => false);
        Assert.True(probe.WaitFor(static n => n == 1, TimeSpan.FromSeconds(30)), "The grandchild did not start.");

        tree.Kill();

        Assert.True(
            probe.WaitFor(static n => n == 0, TimeSpan.FromSeconds(10)),
            "The grandchild in the job survived a failed termination of the job."
        );
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FindInPath_NonExecutableFileFirst_TakesTheExecutableOne()
    {
        Skip.If(OperatingSystem.IsWindows(), "The execute permission and the PATH search of setsid are Unix only.");
        var first = CreateDirectory("first");
        var second = CreateDirectory("second");
        WriteFile(Path.Combine(first, "tool"), "#!/bin/sh\n", executable: false);
        var expected = WriteFile(Path.Combine(second, "tool"), "#!/bin/sh\n", executable: true);

        var found = UnixProcessGroup.FindInPath("tool", first + ":" + second);

        Assert.Equal(expected, found);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("#!/bin/sh\necho hi\n", false, false)]
    [InlineData("echo hi\n", true, false)]
    [InlineData("#!/this/interpreter/does/not/exist\n", true, false)]
    [InlineData("#!/bin/sh\necho hi\n", true, true)]
    [InlineData("#!  /bin/sh -e\necho hi\n", true, true)]
    public void TryWrap_ProgramThatExecMayRefuse_WrapsOnlyWhatExecAccepts(
        string content,
        bool executable,
        bool wrapped
    )
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "setsid wraps programs on Linux only.");
        var program = WriteFile(Path.Combine(_directory, "program"), content, executable);
        var startInfo = new ProcessStartInfo { FileName = program, Arguments = "a" };

        var result = UnixProcessGroup.TryWrap(startInfo, out var original);
        original.Restore(startInfo);

        Assert.Equal(wrapped, result);
        Assert.Equal(program, startInfo.FileName);
    }

    private static ProcessStartInfo QuickExit() =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c exit 0", UseShellExecute = false }
            : new ProcessStartInfo { FileName = "true", UseShellExecute = false };

    private string CreateDirectory(string name) => Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;

    private static string WriteFile(string path, string content, bool executable)
    {
        File.WriteAllText(path, content, new UTF8Encoding(false));
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, executable ? mode | UnixFileMode.UserExecute : mode);
        return path;
    }
}
