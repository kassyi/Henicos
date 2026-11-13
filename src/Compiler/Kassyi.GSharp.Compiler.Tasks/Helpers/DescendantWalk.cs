namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>One row of a process table: the process, the process that created it, and when it was created.</summary>
/// <param name="ProcessId">The process ID.</param>
/// <param name="ParentProcessId">The ID of the process that created it, as the OS recorded it at creation.</param>
/// <param name="StartTime">
/// The creation time in the OS's own unit, or <see langword="null"/> when the table has none.
/// </param>
internal readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, long? StartTime);

/// <summary>
/// Finds the descendants of a process by the parent IDs of a process table and acts on each one top-down, table after
/// table, until a table shows no new descendant.
/// </summary>
/// <remarks>
/// <para>
/// A parent ID is only a number: once its process has ended the number can be reused, and a process whose recorded
/// parent has the same number may belong to someone else. A child therefore counts only when it was created at or after
/// its parent; an older process with that parent ID was created by an earlier owner of the number. The action checks
/// the creation time again on the process it opens or signals, since the number can change hands after the table was
/// read.
/// </para>
/// <para>
/// A descendant that the action has handled keeps its place in the walk even when it is gone from later tables (its
/// children still name it as their parent), so the walk reaches the processes below a parent that was just killed.
/// </para>
/// </remarks>
internal static class DescendantWalk
{
    /// <summary>The most tables one walk reads; each later table only looks for processes started meanwhile.</summary>
    public const int MaxRounds = 8;

    /// <summary>
    /// Visits the descendants of the root and returns the ones the action handled, with their creation time.
    /// </summary>
    /// <param name="rootId">The process whose descendants are wanted.</param>
    /// <param name="rootStart">The root's creation time, in the unit of the table.</param>
    /// <param name="readTable">Reads the current process table.</param>
    /// <param name="act">
    /// Acts on a candidate (stops or kills it) when it was created at or after the given parent creation time, and
    /// returns its creation time; returns <see langword="null"/> when it is gone or is not a descendant.
    /// </param>
    public static Dictionary<int, long> Visit(
        int rootId,
        long rootStart,
        Func<IReadOnlyList<ProcessEntry>> readTable,
        Func<ProcessEntry, long, long?> act
    )
    {
        var handled = new Dictionary<int, long>();
        for (var round = 0; round < MaxRounds; round++)
        {
            if (!VisitTable(rootId, rootStart, readTable(), act, handled))
            {
                break;
            }
        }

        return handled;
    }

    // One breadth-first pass over a table; returns whether it handled a process the earlier passes had not.
    private static bool VisitTable(
        int rootId,
        long rootStart,
        IReadOnlyList<ProcessEntry> table,
        Func<ProcessEntry, long, long?> act,
        Dictionary<int, long> handled
    )
    {
        var children = GroupByParent(table, rootId);

        // The handled descendants start the pass beside the root: a killed one may be gone from this table while its
        // children still name it.
        var pending = new Queue<KeyValuePair<int, long>>(handled);
        pending.Enqueue(new KeyValuePair<int, long>(rootId, rootStart));
        var found = false;
        while (pending.Count > 0)
        {
            var parent = pending.Dequeue();
            if (!children.TryGetValue(parent.Key, out var list))
            {
                continue;
            }

            foreach (var child in list)
            {
                var olderThanParent = child.StartTime is { } start && start < parent.Value;
                if (olderThanParent || handled.ContainsKey(child.ProcessId))
                {
                    continue;
                }

                if (act(child, parent.Value) is { } created)
                {
                    handled.Add(child.ProcessId, created);
                    pending.Enqueue(new KeyValuePair<int, long>(child.ProcessId, created));
                    found = true;
                }
            }
        }

        return found;
    }

    private static Dictionary<int, List<ProcessEntry>> GroupByParent(IReadOnlyList<ProcessEntry> table, int rootId)
    {
        var children = new Dictionary<int, List<ProcessEntry>>();
        foreach (var entry in table)
        {
            if (entry.ProcessId == rootId || entry.ProcessId == entry.ParentProcessId)
            {
                continue;
            }

            if (!children.TryGetValue(entry.ParentProcessId, out var list))
            {
                list = [];
                children.Add(entry.ParentProcessId, list);
            }

            list.Add(entry);
        }

        return children;
    }
}
