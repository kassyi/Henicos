using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// Kills a process and its descendants on Linux and macOS by their parent IDs, for a process that does not lead a
/// process group of its own (no <c>setsid</c>, as on macOS, or a start info that uses <c>ArgumentList</c>).
/// </summary>
/// <remarks>
/// <para>
/// When a process dies its children are moved to another parent, which hides them from a walk by parent IDs. The walk
/// therefore stops (<c>SIGSTOP</c>) the root and each descendant it finds, top-down, and kills (<c>SIGKILL</c>) them
/// only at the end. A stopped process cannot start another one, so the tables converge (<see cref="DescendantWalk"/>).
/// </para>
/// <para>
/// Linux reads <c>/proc/&lt;pid&gt;/stat</c> (parent ID and start time in clock ticks since boot); macOS asks libproc
/// (<c>proc_listchildpids</c>, and <c>proc_pidinfo</c> for the start time), as <c>Process.Kill(true)</c> of .NET does.
/// Each process is checked against its start time again right before it is signalled, so a reused process ID is left
/// alone. A descendant whose parent had already ended before the cancellation was moved away and is not found.
/// </para>
/// </remarks>
internal static class UnixDescendants
{
    private static readonly bool s_isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Kills <paramref name="root"/> (process ID <paramref name="rootId"/>) and its descendants; best effort.
    /// </summary>
    public static void Kill(Process root, int rootId)
    {
        if (HasExited(root) || ReadStartTime(rootId) is not { } rootStart)
        {
            ProcessTree.KillProcessOnly(root);
            return;
        }

        _ = UnixLibc.Send(rootId, UnixLibc.Stop);
        var stopped = DescendantWalk.Visit(
            rootId,
            rootStart,
            () => ReadTable(rootId),
            static (entry, parentStart) => TryStop(entry.ProcessId, parentStart)
        );

        foreach (var processId in stopped.Keys)
        {
            _ = UnixLibc.Send(processId, UnixLibc.Kill);
        }

        ProcessTree.KillProcessOnly(root);
    }

    private static long? TryStop(int processId, long parentStart)
    {
        if (ReadStartTime(processId) is not { } start || start < parentStart)
        {
            return null;
        }

        return UnixLibc.Send(processId, UnixLibc.Stop) ? start : null;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // Disposed or never started: there is no process to walk from.
            return true;
        }
    }

    private static IReadOnlyList<ProcessEntry> ReadTable(int rootId) =>
        s_isMacOS ? MacProcessTable.ReadSubtree(rootId) : LinuxProcessTable.ReadAll();

    private static long? ReadStartTime(int processId) =>
        s_isMacOS ? MacProcessTable.ReadStartTime(processId) : LinuxProcessTable.ReadStartTime(processId);
}
