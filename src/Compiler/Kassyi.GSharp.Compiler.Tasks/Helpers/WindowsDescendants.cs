using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// Kills the descendants of a process on Windows by the parent IDs of a Toolhelp snapshot, for the processes that the
/// job object of <see cref="ProcessTree"/> does not hold: the ones the child started before it joined the job, and
/// every descendant when Windows refused the job.
/// </summary>
/// <remarks>
/// A candidate counts only when it was created at or after its parent and before the snapshot was taken, so a process
/// that took over a reused process ID, before or after the snapshot, is left alone (<see cref="DescendantWalk"/>).
/// Windows keeps the parent ID of a process after its parent ended, so the walk reaches the processes below a killed
/// parent.
/// </remarks>
internal static class WindowsDescendants
{
    private const uint SnapProcess = 0x2;
    private const uint ProcessTerminate = 0x1;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Kills every descendant of <paramref name="root"/> (process ID <paramref name="rootId"/>) that is still running
    /// and not in <paramref name="job"/>, which the caller has terminated; best effort.
    /// </summary>
    public static void Kill(Process root, int rootId, WindowsJobObject? job)
    {
        if (ReadCreationTime(root) is not { } rootStart)
        {
            return;
        }

        long snapshotTime = 0;
        DescendantWalk.Visit(
            rootId,
            rootStart,
            () => ReadTable(out snapshotTime),
            (entry, parentStart) => TryTerminate(entry.ProcessId, parentStart, snapshotTime, job)
        );
    }

    private static long? ReadCreationTime(Process root)
    {
        try
        {
            return GetProcessTimes(root.Handle, out var created, out _, out _, out _) ? created : null;
        }
        catch (InvalidOperationException)
        {
            // The caller disposed the process: its creation time, which guards the walk against reused IDs, is gone.
            return null;
        }
    }

    private static IReadOnlyList<ProcessEntry> ReadTable(out long snapshotTime)
    {
        var table = new List<ProcessEntry>();
        GetSystemTimeAsFileTime(out snapshotTime);
        using var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot.IsInvalid)
        {
            return table;
        }

        // The loop ends at ERROR_NO_MORE_FILES; any other failure ends this table early, and the next round of the walk
        // reads a new one. Nothing may be thrown from here: the walk runs in a cancellation callback.
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
        {
            table.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, StartTime: null));
        }

        return table;
    }

    private static long? TryTerminate(int processId, long parentStart, long snapshotTime, WindowsJobObject? job)
    {
        using var process = OpenProcess(ProcessTerminate | ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid
            || !GetProcessTimes(process, out var created, out _, out _, out _)
            || created < parentStart
            || created > snapshotTime)
        {
            return null;
        }

        // A process of the job was terminated with it; one that ended meanwhile cannot be terminated. Either still
        // leads the walk to its children.
        if (job is null || !job.Contains(process))
        {
            _ = TerminateProcess(process, uint.MaxValue);
        }

        return created;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out long creation,
        out long exit,
        out long kernel,
        out long user
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr process,
        out long creation,
        out long exit,
        out long kernel,
        out long user
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void GetSystemTimeAsFileTime(out long time);

    // PROCESSENTRY32W
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }
}
