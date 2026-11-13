using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// A started process together with every process it starts, so that a cancellation stops the compiler drivers
/// (<c>cmd.exe</c>, <c>nvcc</c>, <c>g++</c>) and the compilers they run, which hold the output pipes until they end.
/// </summary>
/// <remarks>
/// <para>
/// Windows: the process joins a <see cref="WindowsJobObject"/> right after it starts, and a cancellation terminates the
/// job. A process the child starts before it has joined (between <c>CreateProcess</c> returning in
/// <see cref="Process.Start()"/> and the assignment; <see cref="Process"/> cannot start a process suspended) stays
/// outside the job, so the cancellation also walks the process table (<see cref="WindowsDescendants"/>), which is also
/// what stops the tree when Windows refuses the job.
/// </para>
/// <para>
/// Linux: the process starts as the leader of its own process group (<see cref="UnixProcessGroup"/>), so the group
/// holds every descendant from the start. Where that is not possible (macOS, which has no <c>setsid</c>, or a start
/// info that uses <c>ArgumentList</c>), the cancellation stops the tree by a walk of the process table
/// (<see cref="UnixDescendants"/>).
/// </para>
/// <para>A normal end changes nothing: descendants that outlive the process keep running, as before.</para>
/// <para>
/// Not covered on Linux: a build host that dies without cancelling (killed, or its terminal closed) leaves the
/// compilers running. <c>setsid</c> also takes them out of the terminal's session, so Ctrl+C and the hang-up of the
/// terminal no longer reach them directly; they rely on the cancellation that MSBuild sends. Windows covers this case
/// with the kill-on-close limit of the job. <c>PR_SET_PDEATHSIG</c> is no substitute: it fires when the thread that
/// started the child ends, which for a thread-pool thread of the host is any time, and it reaches the direct child
/// only.
/// </para>
/// </remarks>
internal sealed class ProcessTree : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly Process _process;
    private readonly WindowsJobObject? _job;
    private readonly bool _ownsGroup;

    // Read once at the start: a cancellation may arrive after the caller disposed the Process, which forgets its ID.
    private readonly int _processId;

    private ProcessTree(Process process, WindowsJobObject? job, bool ownsGroup)
    {
        _process = process;
        _job = job;
        _ownsGroup = ownsGroup;
        _processId = process.Id;
    }

    /// <summary>Starts <paramref name="process"/> so that <see cref="Kill"/> reaches its descendants.</summary>
    /// <exception cref="InvalidOperationException">The process did not start.</exception>
    public static ProcessTree Start(Process process) =>
        s_isWindows ? StartInJob(process) : StartInGroup(process);

    /// <summary>
    /// Stops the process and its descendants; best effort, as a process may end at the same time. Never throws, also
    /// after the caller disposed the <see cref="Process"/>, since it runs in a cancellation callback.
    /// </summary>
    public void Kill()
    {
        if (s_isWindows)
        {
            // The job holds every descendant started after the assignment; the walk finds the ones started before it
            // and, when Windows refused the job, all of them.
            if (_job is null || !_job.Terminate())
            {
                KillProcessOnly(_process);
            }

            WindowsDescendants.Kill(_process, _processId, _job);
            return;
        }

        if (_ownsGroup)
        {
            UnixProcessGroup.Kill(_process, _processId);
            return;
        }

        UnixDescendants.Kill(_process, _processId);
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
