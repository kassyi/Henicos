#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// A uniquely named copy of a program that waits (<c>ping.exe</c> on Windows; elsewhere <c>/bin/sh</c> running
/// <c>sleep</c>, since <c>sleep</c> itself may be a multi-call binary that dispatches on its own name), started as a
/// grandchild through a shell, so a test can tell whether that grandchild still runs. Disposing the probe kills every
/// process of that name, with the children of the copies on Linux, and deletes the copy, so a failing test leaves no
/// process behind.
/// </summary>
internal sealed class DescendantProcessProbe : IDisposable
{
    private readonly string _directory;
    private readonly string _executable;

    private DescendantProcessProbe(string directory, string name)
    {
        _directory = directory;
        Name = name;
        _executable = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.Copy(SleeperPath(), _executable);
    }

    /// <summary>Gets the process name of the copy (at most 15 characters, the length Linux keeps).</summary>
    public string Name { get; }

    /// <summary>Copies the sleeping program under a new unique name.</summary>
    public static DescendantProcessProbe Create()
    {
        var name = string.Concat("gst", Guid.NewGuid().ToString("N").AsSpan(0, 11));
        var directory = Path.Combine(Path.GetTempPath(), "gsharp-process-tree-" + name);
        Directory.CreateDirectory(directory);
        return new DescendantProcessProbe(directory, name);
    }

    /// <summary>
    /// A shell that starts the copy for <paramref name="seconds"/> seconds and waits for it; the copy inherits the
    /// redirected output pipes. Built with <see cref="ProcessStartInfo.Arguments"/>, as the compile tasks build theirs.
    /// </summary>
    public ProcessStartInfo WaitingShell(int seconds) =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{_executable}\" -n {seconds + 1} 127.0.0.1\""
            }
            : new ProcessStartInfo
            {
                FileName = "sh",
                Arguments = $"-c \"'{_executable}' -c 'sleep {seconds}; :'; true\""
            };

    /// <summary>
    /// A shell that starts the copy for <paramref name="seconds"/> seconds without its output pipes and exits at once,
    /// as a compiler starts a server that outlives it (<c>mspdbsrv.exe</c> of MSVC).
    /// </summary>
    public ProcessStartInfo DetachingShell(int seconds) =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"Start-Process -WindowStyle Hidden"
                    + $" -FilePath '{_executable}' -ArgumentList '-n','{seconds + 1}','127.0.0.1'\""
            }
            : new ProcessStartInfo
            {
                FileName = "sh",
                Arguments = $"-c \"'{_executable}' -c 'sleep {seconds}; :' >/dev/null 2>&1 </dev/null &\""
            };

    /// <summary>Gets the number of running processes of the copy.</summary>
    public int CountRunning()
    {
        var processes = Process.GetProcessesByName(Name);
        var running = 0;
        foreach (var process in processes)
        {
            if (!HasExited(process))
            {
                running++;
            }

            process.Dispose();
        }

        return running;
    }

    /// <summary>
    /// Waits until <see cref="CountRunning"/> satisfies <paramref name="condition"/> or the timeout passes.
    /// </summary>
    public bool WaitFor(Func<int, bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition(CountRunning()))
        {
            if (stopwatch.Elapsed > timeout)
            {
                return false;
            }

            Thread.Sleep(50);
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var process in Process.GetProcessesByName(Name))
        {
            try
            {
                KillChildrenOnLinux(process.Id);
                process.Kill();
                process.WaitForExit(10_000);
            }
            // smell-scan: ignore swallowed-exception -- the process ended between the listing and the kill, which is
            // what the cleanup wants
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Console.WriteLine($"Probe process {process.Id} already gone: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        DeleteDirectory();
    }

    private void DeleteDirectory()
    {
        // A process killed a moment ago may still hold its image file open on Windows.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Retrying the removal of {_directory}: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A process that cannot be opened any more is gone.
            Console.WriteLine($"Probe process {process.Id} cannot be read: {ex.Message}");
            return true;
        }
    }

    private static string SleeperPath() =>
        OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "PING.EXE") : "/bin/sh";

    // The copy of sh waits for its sleep; killing the copy alone would leave that sleep running.
    private static void KillChildrenOnLinux(int parentId)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var processId) || ReadParentId(directory) != parentId)
            {
                continue;
            }

            try
            {
                using var child = Process.GetProcessById(processId);
                child.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                // The child ended between the listing and the kill.
                Console.WriteLine($"Probe child {processId} already gone: {ex.Message}");
            }
        }
    }

    // Field 4 of /proc/<pid>/stat, counted after the closing parenthesis of the command name.
    private static int? ReadParentId(string directory)
    {
        try
        {
            var stat = File.ReadAllText(Path.Combine(directory, "stat"));
            var fields = stat.AsSpan(stat.LastIndexOf(')') + 1).TrimStart(' ');
            fields = fields.Slice(fields.IndexOf(' ') + 1);
            var end = fields.IndexOf(' ');
            return int.TryParse(end < 0 ? fields : fields.Slice(0, end), out var parentId) ? parentId : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process ended or belongs to another user: not a child of the probe.
            Console.WriteLine($"Skipping {directory}: {ex.Message}");
            return null;
        }
    }
}
