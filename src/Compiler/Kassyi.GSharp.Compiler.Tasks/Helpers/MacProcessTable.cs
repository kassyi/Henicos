using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// Reads children and start times of processes through libproc on macOS, as .NET's own process code does.
/// </summary>
internal static class MacProcessTable
{
    // proc_pidinfo flavor PROC_PIDTBSDINFO, which fills struct proc_bsdinfo.
    private const int BsdInfoFlavor = 3;

    // struct proc_bsdinfo: 136 bytes; pbi_start_tvsec at 120, pbi_start_tvusec at 128.
    private const int BsdInfoSize = 136;
    private const int StartSecondsOffset = 120;
    private const int StartMicrosecondsOffset = 128;

    // Room for processes started between the count and the listing.
    private const int SpareSlots = 16;

    /// <summary>
    /// Reads the descendants of <paramref name="rootId"/> by asking for the children of each process found.
    /// </summary>
    public static IReadOnlyList<ProcessEntry> ReadSubtree(int rootId)
    {
        var table = new List<ProcessEntry>();
        var pending = new Queue<int>();
        var seen = new HashSet<int> { rootId };
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();
            foreach (var childId in ListChildren(parentId))
            {
                if (seen.Add(childId))
                {
                    table.Add(new ProcessEntry(childId, parentId, ReadStartTime(childId)));
                    pending.Enqueue(childId);
                }
            }
        }

        return table;
    }

    /// <summary>Reads the start time of one process, in microseconds since the epoch.</summary>
    public static long? ReadStartTime(int processId)
    {
        var info = new byte[BsdInfoSize];
        if (ProcPidInfo(processId, BsdInfoFlavor, 0, info, BsdInfoSize) != BsdInfoSize)
        {
            return null;
        }

        var seconds = BitConverter.ToInt64(info, StartSecondsOffset);
        var microseconds = BitConverter.ToInt64(info, StartMicrosecondsOffset);
        return seconds * 1_000_000 + microseconds;
    }

    // proc_listchildpids takes the buffer size in bytes and returns a count of process IDs (libproc divides the byte
    // count by sizeof(pid_t)); a call without a buffer returns the count to expect. A full buffer may have cut the
    // list, so the call is repeated with a larger one.
    private static int[] ListChildren(int parentId)
    {
        var count = ProcListChildPids(parentId, null, 0);
        for (var attempt = 0; count > 0 && attempt < 4; attempt++)
        {
            var buffer = new int[count + SpareSlots];
            count = ProcListChildPids(parentId, buffer, buffer.Length * sizeof(int));
            if (count < buffer.Length)
            {
                var children = new int[Math.Max(count, 0)];
                Array.Copy(buffer, children, children.Length);
                return children;
            }
        }

        return [];
    }

    [DllImport("libproc", EntryPoint = "proc_listchildpids", SetLastError = true)]
    private static extern int ProcListChildPids(int parentId, [Out] int[]? buffer, int bufferSize);

    [DllImport("libproc", EntryPoint = "proc_pidinfo", SetLastError = true)]
    private static extern int ProcPidInfo(
        int processId,
        int flavor,
        ulong argument,
        [Out] byte[] buffer,
        int bufferSize
    );
}
