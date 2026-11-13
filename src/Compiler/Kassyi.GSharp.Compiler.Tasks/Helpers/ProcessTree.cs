using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// A started process together with every process it starts, so that a cancellation stops the compiler drivers
/// (<c>cmd.exe</c>, <c>nvcc</c>, <c>g++</c>) and the compilers they run, which hold the output pipes until they end.
/// </summary>
/// <remarks>
/// <para>
/// Windows: the process joins a <see cref="WindowsJobObject"/> right after it starts. A process it starts before it has
/// joined stays outside the job; the window is the few instructions between <c>CreateProcess</c> returning in
/// <see cref="Process.Start()"/> and the assignment, while the child is still loading. <see cref="Process"/> cannot
/// start a process suspended, which would close the window.
/// </para>
/// <para>
/// Linux: the process starts as the leader of its own process group (<see cref="UnixProcessGroup"/>), so there is no
/// such window. Where that is not possible, and where Windows refuses the job, only the process itself is stopped.
/// </para>
/// <para>A normal end changes nothing: descendants that outlive the process keep running, as before.</para>
/// </remarks>
internal sealed class ProcessTree : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly Process _process;
    private readonly WindowsJobObject? _job;
    private readonly bool _ownsGroup;

    private ProcessTree(Process process, WindowsJobObject? job, bool ownsGroup)
    {
        _process = process;
        _job = job;
        _ownsGroup = ownsGroup;
    }

    /// <summary>Starts <paramref name="process"/> so that <see cref="Kill"/> reaches its descendants.</summary>
    /// <exception cref="InvalidOperationException">The process did not start.</exception>
    public static ProcessTree Start(Process process) =>
        s_isWindows ? StartInJob(process) : StartInGroup(process);

    /// <summary>Stops the process and its descendants; best effort, as a process may end at the same time.</summary>
    public void Kill()
    {
        if (_job is not null && _job.Terminate())
        {
            return;
        }

        if (_ownsGroup)
        {
            UnixProcessGroup.Kill(_process);
            return;
        }

        KillProcessOnly(_process);
    }

    /// <inheritdoc />
    public void Dispose() => _job?.Dispose();

    /// <summary>Stops <paramref name="process"/> alone, ignoring a process that has already ended.</summary>
    internal static void KillProcessOnly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        // smell-scan: ignore swallowed-exception -- the process exited or was disposed between the check and the kill,
        // so there is nothing left to stop
        catch (InvalidOperationException)
        {
        }
        // smell-scan: ignore swallowed-exception -- access denied or already terminating; killing is best-effort and
        // the caller observes the cancellation anyway
        catch (Win32Exception)
        {
        }
    }

    private static ProcessTree StartInJob(Process process)
    {
        var job = WindowsJobObject.TryCreate();
        try
        {
            StartOrThrow(process);
        }
        catch
        {
            job?.Dispose();
            throw;
        }

        if (job is not null && !job.TryAssign(process))
        {
            job.Dispose();
            job = null;
        }

        return new ProcessTree(process, job, ownsGroup: false);
    }

    private static ProcessTree StartInGroup(Process process)
    {
        var startInfo = process.StartInfo;
        if (!UnixProcessGroup.TryWrap(startInfo, out var original))
        {
            StartOrThrow(process);
            return new ProcessTree(process, job: null, ownsGroup: false);
        }

        try
        {
            StartOrThrow(process);
            return new ProcessTree(process, job: null, ownsGroup: true);
        }
        catch (InvalidOperationException)
        {
            // The caller filled ArgumentList, which cannot be combined with the wrapper's Arguments; nothing started.
            original.Restore(startInfo);
            StartOrThrow(process);
            return new ProcessTree(process, job: null, ownsGroup: false);
        }
        finally
        {
            original.Restore(startInfo);
        }
    }

    private static void StartOrThrow(Process process)
    {
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {process.StartInfo.FileName}");
        }
    }
}
