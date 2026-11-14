namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>Reads the parent ID and start time of processes from <c>/proc</c> on Linux.</summary>
internal static class LinuxProcessTable
{
    // Fields of /proc/<pid>/stat after the closing parenthesis of the command name: field 3 (state) has index 0.
    private const int ParentIdIndex = 1;
    private const int StartTimeIndex = 19;

    /// <summary>Reads every process that is still readable.</summary>
    public static IReadOnlyList<ProcessEntry> ReadAll()
    {
        var table = new List<ProcessEntry>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(directory), out var processId)
                && ReadStat(processId) is { } stat
                && TryParseStat(stat, out var parentId, out var start))
            {
                table.Add(new ProcessEntry(processId, parentId, start));
            }
        }

        return table;
    }

    /// <summary>Reads the start time of one process, in clock ticks since boot.</summary>
    public static long? ReadStartTime(int processId) =>
        ReadStat(processId) is { } stat && TryParseStat(stat, out _, out var start) ? start : null;

    /// <summary>Reads the parent ID and the start time from the text of <c>/proc/&lt;pid&gt;/stat</c>.</summary>
    /// <remarks>
    /// The command name may hold spaces and parentheses, so the fields are counted after its last <c>)</c>.
    /// </remarks>
    public static bool TryParseStat(string stat, out int parentId, out long startTime)
    {
        parentId = 0;
        startTime = 0;
        var close = stat.LastIndexOf(')');
        if (close < 0)
        {
            return false;
        }

        var rest = stat.AsSpan(close + 1);
        var parentRead = false;
        for (var index = 0; index <= StartTimeIndex; index++)
        {
            rest = rest.TrimStart(' ');
            var end = rest.IndexOf(' ');
            var field = end < 0 ? rest : rest.Slice(0, end);
            if (field.Length == 0)
            {
                return false;
            }

            if (index == ParentIdIndex)
            {
                parentRead = int.TryParse(field.ToString(), out parentId);
            }
            else if (index == StartTimeIndex)
            {
                return parentRead && long.TryParse(field.ToString(), out startTime);
            }

            rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end);
        }

        return false;
    }

    private static string? ReadStat(int processId)
    {
        try
        {
            return File.ReadAllText("/proc/" + processId + "/stat");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process ended or belongs to another user: it is not one to walk.
            return null;
        }
    }
}
