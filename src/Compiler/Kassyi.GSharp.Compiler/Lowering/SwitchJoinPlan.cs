namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// A reconvergence join point inside a thread-dependent <c>switch</c> section entered from another section
/// (<c>goto case</c>, <c>goto default</c>, or a cross-section <c>goto</c>).
/// </summary>
/// <param name="Order">Topological order of the join point (<c>1 .. k</c>).</param>
/// <param name="Section">Zero-based index of the target section in the switch.</param>
/// <param name="Position">
/// <c>0</c> for the section head (<c>goto case</c> / <c>goto default</c>); <c>canonicalIndex + 1</c> for a label at
/// canonical statement index <c>canonicalIndex</c> in the section's statement list.
/// </param>
internal sealed record SwitchJoin(int Order, int Section, int Position);

/// <summary>
/// Planned reconvergence joins for a thread-dependent <c>switch</c>: ordered joins, the earliest held join order per
/// <c>(section, segment)</c>, and any jumps that form a loop through a join.
/// </summary>
/// <param name="Joins">Planned join points in ascending <see cref="SwitchJoin.Order"/> (<c>J_1 .. J_k</c>).</param>
/// <param name="FirstHeld">
/// <c>FirstHeld[section][segment]</c>: minimum <see cref="SwitchJoin.Order"/> reachable from that segment, or
/// <c>Joins.Length + 1</c> when none is reachable.
/// </param>
/// <param name="Rejected">
/// Cross-section jumps on a cycle, and jumps back inside a section across one of its joins (when non-empty, no joins
/// are planned).
/// </param>
internal sealed record SwitchJoinPlan(
    ImmutableArray<SwitchJoin> Joins,
    ImmutableArray<ImmutableArray<int>> FirstHeld,
    ImmutableArray<GotoStatementSyntax> Rejected)
{
    public static readonly SwitchJoinPlan Empty = new(
        ImmutableArray<SwitchJoin>.Empty,
        ImmutableArray<ImmutableArray<int>>.Empty,
        ImmutableArray<GotoStatementSyntax>.Empty);
}

/// <summary>
/// Plans reconvergence join points for second-entry jumps into sections of a thread-dependent <c>switch</c>.
/// </summary>
internal static class SwitchJoinPlanner
{
    /// <summary>
    /// Plans join points for <paramref name="switchStatement"/>, or returns the jumps that form a loop through a join in
    /// <see cref="SwitchJoinPlan.Rejected"/> with no planned joins.
    /// </summary>
    internal static SwitchJoinPlan Plan(
        SwitchStatementSyntax switchStatement,
        Func<GotoStatementSyntax, int> sectionOfGotoCase,
        Func<GotoStatementSyntax, bool>? isIncluded = null)
    {
        var sectionCount = switchStatement.Sections.Count;
        if (sectionCount == 0)
        {
            return SwitchJoinPlan.Empty;
        }

        var canonicalIndices = new int[sectionCount][];
        for (var s = 0; s < sectionCount; s++)
        {
            canonicalIndices[s] = JumpRegionPlanner.BuildCanonicalLabelIndices(switchStatement.Sections[s].Statements);
        }

        var jumps = CollectCrossSectionJumps(switchStatement, sectionOfGotoCase, isIncluded, canonicalIndices);
        if (jumps.IsEmpty)
        {
            return SwitchJoinPlan.Empty;
        }

        var nodes = BuildSortedJoinNodes(jumps);
        var joinCount = nodes.Length;
        var sectionNodes = GroupNodesBySection(sectionCount, nodes);
        var nodeByKey = BuildNodeLookup(nodes);
        var resolvedJumps = ResolveJumps(jumps, nodes, sectionNodes, nodeByKey);
        var adjacency = BuildJoinGraph(joinCount, sectionNodes, resolvedJumps);

        var rejected = FindCyclicJumps(joinCount, resolvedJumps, sectionNodes, adjacency);
        AddBackJumpsAcrossJoins(
            rejected,
            switchStatement,
            sectionOfGotoCase,
            nodes,
            sectionNodes,
            canonicalIndices,
            isIncluded);
        if (rejected.Count > 0)
        {
            return new SwitchJoinPlan(
                ImmutableArray<SwitchJoin>.Empty,
                ImmutableArray<ImmutableArray<int>>.Empty,
                rejected.ToImmutable());
        }

        var orderByNode = ComputeTopologicalOrders(nodes, adjacency, out var orderedJoins);
        var firstHeld = ComputeFirstHeld(sectionCount, joinCount, sectionNodes, resolvedJumps, orderByNode);
        return new SwitchJoinPlan(orderedJoins, firstHeld, ImmutableArray<GotoStatementSyntax>.Empty);
    }

    /// <summary>
    /// Returns the zero-based segment index of statement <paramref name="statementIndex"/> in
    /// <paramref name="section"/>.
    /// </summary>
    internal static int FindSegmentForStatement(
        ImmutableArray<SwitchJoin> joins,
        int section,
        int statementIndex) =>
        joins
            .AsStackEnumerable()
            .Count((section, statementIndex), static (join, state) =>
                join.Section == state.section && join.Position <= state.statementIndex + 1
            );

    private static ImmutableArray<CollectedJump> CollectCrossSectionJumps(
        SwitchStatementSyntax switchStatement,
        Func<GotoStatementSyntax, int> sectionOfGotoCase,
        Func<GotoStatementSyntax, bool>? isIncluded,
        int[][] canonicalIndices)
    {
        var jumps = ImmutableArray.CreateBuilder<CollectedJump>();
        var sectionCount = switchStatement.Sections.Count;
        for (var s = 0; s < sectionCount; s++)
        {
            var statements = switchStatement.Sections[s].Statements;
            for (var k = 0; k < statements.Count; k++)
            {
                foreach (var node in statements[k].DescendantNodesAndSelf(static n =>
                             n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                {
                    if (node is not GotoStatementSyntax gotoStmt)
                    {
                        continue;
                    }

                    if (isIncluded != null && !isIncluded(gotoStmt))
                    {
                        continue;
                    }

                    if (gotoStmt.IsKind(SyntaxKind.GotoCaseStatement, SyntaxKind.GotoDefaultStatement))
                    {
                        if (!ReconvergenceSyntax.EnclosingSwitchIs(gotoStmt, switchStatement))
                        {
                            continue;
                        }

                        var targetSection = sectionOfGotoCase(gotoStmt);
                        if (targetSection >= 0 && targetSection < sectionCount)
                        {
                            jumps.Add(new CollectedJump(s, k, gotoStmt, targetSection, 0));
                        }
                    }
                    else if (gotoStmt.IsKind(SyntaxKind.GotoStatement) &&
                             gotoStmt.Expression is IdentifierNameSyntax { Identifier.ValueText: var labelName } &&
                             ReconvergenceSyntax.FindLabel(gotoStmt, labelName) is { } targetLabel)
                    {
                        for (var t = 0; t < sectionCount; t++)
                        {
                            if (t == s)
                            {
                                continue;
                            }

                            var canonicalIndex = JumpRegionPlanner.FindCanonicalStatementIndexForLabel(
                                switchStatement.Sections[t].Statements,
                                targetLabel,
                                canonicalIndices[t]);
                            if (canonicalIndex >= 0)
                            {
                                jumps.Add(new CollectedJump(s, k, gotoStmt, t, canonicalIndex + 1));
                                break;
                            }
                        }
                    }
                }
            }
        }

        // A goto case to its own section is a plain loop back to the section head and needs no join; only when lanes of
        // another section also jump to that head does it loop through the join there
        var collected = ImmutableArray.CreateBuilder<CollectedJump>(jumps.Count);
        foreach (var jump in jumps)
        {
            if (jump.SourceSection != jump.TargetSection ||
                jump.TargetPosition != 0 ||
                jumps
                    .AsStackEnumerable()
                    .Any(jump.TargetSection, static (other, section) =>
                        other.TargetSection == section && other.TargetPosition == 0 && other.SourceSection != section
                    ))
            {
                collected.Add(jump);
            }
        }

        return collected.ToImmutable();
    }

    private static ImmutableArray<(int Section, int Position)> BuildSortedJoinNodes(
        ImmutableArray<CollectedJump> jumps)
    {
        var nodes = ImmutableArray.CreateBuilder<(int Section, int Position)>();
        foreach (var jump in jumps)
        {
            var key = (jump.TargetSection, jump.TargetPosition);
            if (!nodes.Contains(key))
            {
                nodes.Add(key);
            }
        }

        nodes.Sort(static (a, b) =>
        {
            var cmp = a.Section.CompareTo(b.Section);
            return cmp != 0 ? cmp : a.Position.CompareTo(b.Position);
        });
        return nodes.ToImmutable();
    }

    private static ImmutableArray<int>[] GroupNodesBySection(
        int sectionCount,
        ImmutableArray<(int Section, int Position)> nodes)
    {
        var builders = new ImmutableArray<int>.Builder[sectionCount];
        for (var s = 0; s < sectionCount; s++)
        {
            builders[s] = ImmutableArray.CreateBuilder<int>();
        }

        for (var u = 0; u < nodes.Length; u++)
        {
            builders[nodes[u].Section].Add(u);
        }

        var sectionNodes = new ImmutableArray<int>[sectionCount];
        for (var s = 0; s < sectionCount; s++)
        {
            sectionNodes[s] = builders[s].ToImmutable();
        }

        return sectionNodes;
    }

    private static Dictionary<(int Section, int Position), int> BuildNodeLookup(
        ImmutableArray<(int Section, int Position)> nodes)
    {
        var lookup = new Dictionary<(int Section, int Position), int>(nodes.Length);
        for (var u = 0; u < nodes.Length; u++)
        {
            lookup[nodes[u]] = u;
        }

        return lookup;
    }

    private static ResolvedJump[] ResolveJumps(
        ImmutableArray<CollectedJump> jumps,
        ImmutableArray<(int Section, int Position)> nodes,
        ImmutableArray<int>[] sectionNodes,
        Dictionary<(int Section, int Position), int> nodeByKey)
    {
        var resolved = new ResolvedJump[jumps.Length];
        for (var i = 0; i < jumps.Length; i++)
        {
            var jump = jumps[i];
            var segment = CountJoinsUpTo(nodes, sectionNodes[jump.SourceSection], jump.StatementIndex);
            var targetNode = nodeByKey[(jump.TargetSection, jump.TargetPosition)];
            resolved[i] = new ResolvedJump(jump.SourceSection, segment, jump.Jump, targetNode);
        }

        return resolved;
    }

    private static ImmutableArray<int>[] BuildJoinGraph(
        int joinCount,
        ImmutableArray<int>[] sectionNodes,
        ResolvedJump[] resolvedJumps)
    {
        var builders = new ImmutableArray<int>.Builder[joinCount];
        for (var u = 0; u < joinCount; u++)
        {
            builders[u] = ImmutableArray.CreateBuilder<int>();
        }

        foreach (var nodesInSection in sectionNodes)
        {
            for (var i = 0; i + 1 < nodesInSection.Length; i++)
            {
                AddEdge(builders, nodesInSection[i], nodesInSection[i + 1]);
            }
        }

        foreach (var jump in resolvedJumps)
        {
            if (jump.Segment > 0)
            {
                var prevNode = sectionNodes[jump.SourceSection][jump.Segment - 1];
                AddEdge(builders, prevNode, jump.TargetNode);
            }
        }

        var adjacency = new ImmutableArray<int>[joinCount];
        for (var u = 0; u < joinCount; u++)
        {
            adjacency[u] = builders[u].ToImmutable();
        }

        return adjacency;
    }

    private static void AddEdge(ImmutableArray<int>.Builder[] adjacency, int from, int to)
    {
        if (!adjacency[from].Contains(to))
        {
            adjacency[from].Add(to);
        }
    }

    private static ImmutableArray<GotoStatementSyntax>.Builder FindCyclicJumps(
        int joinCount,
        ResolvedJump[] resolvedJumps,
        ImmutableArray<int>[] sectionNodes,
        ImmutableArray<int>[] adjacency)
    {
        var rejected = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        var visited = new bool[joinCount];
        var queue = new Queue<int>();

        foreach (var jump in resolvedJumps)
        {
            if (jump.Segment == 0)
            {
                continue;
            }

            var prevNode = sectionNodes[jump.SourceSection][jump.Segment - 1];
            if (IsReachable(jump.TargetNode, prevNode, adjacency, visited, queue) && !rejected.Contains(jump.Jump))
            {
                rejected.Add(jump.Jump);
            }
        }

        return rejected;
    }

    // A goto back to a label of its own section that lies before one of the section's joins passes that join again:
    // its lanes arrive there a second time with the switch region on top, so they form a loop through the join just as
    // a cycle of cross-section jumps does. A goto case or goto default to its own section goes back to the section head
    // and so does the same past every join of the section before it
    private static void AddBackJumpsAcrossJoins(
        ImmutableArray<GotoStatementSyntax>.Builder rejected,
        SwitchStatementSyntax switchStatement,
        Func<GotoStatementSyntax, int> sectionOfGotoCase,
        ImmutableArray<(int Section, int Position)> nodes,
        ImmutableArray<int>[] sectionNodes,
        int[][] canonicalIndices,
        Func<GotoStatementSyntax, bool>? isIncluded)
    {
        for (var s = 0; s < switchStatement.Sections.Count; s++)
        {
            if (sectionNodes[s].IsEmpty)
            {
                continue;
            }

            var statements = switchStatement.Sections[s].Statements;
            for (var k = 0; k < statements.Count; k++)
            {
                foreach (var node in statements[k].DescendantNodesAndSelf(static n =>
                             n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                {
                    if (node is not GotoStatementSyntax gotoStmt || (isIncluded != null && !isIncluded(gotoStmt)))
                    {
                        continue;
                    }

                    var l = FindTargetInOwnSection(
                        switchStatement,
                        s,
                        gotoStmt,
                        sectionOfGotoCase,
                        canonicalIndices[s]);
                    if (l >= -1 &&
                        l <= k &&
                        CountJoinsUpTo(nodes, sectionNodes[s], l) < CountJoinsUpTo(nodes, sectionNodes[s], k) &&
                        !rejected.Contains(gotoStmt))
                    {
                        rejected.Add(gotoStmt);
                    }
                }
            }
        }
    }

    // The canonical index in section s of the label that gotoStmt goes to, -1 for the head of section s (a goto case or
    // goto default to it), -2 when the goto leaves section s
    private static int FindTargetInOwnSection(
        SwitchStatementSyntax switchStatement,
        int s,
        GotoStatementSyntax gotoStmt,
        Func<GotoStatementSyntax, int> sectionOfGotoCase,
        int[] canonicalIndices)
    {
        if (gotoStmt.IsKind(SyntaxKind.GotoCaseStatement, SyntaxKind.GotoDefaultStatement))
        {
            return ReconvergenceSyntax.EnclosingSwitchIs(gotoStmt, switchStatement) && sectionOfGotoCase(gotoStmt) == s
                ? -1
                : -2;
        }

        if (gotoStmt.IsKind(SyntaxKind.GotoStatement) &&
            gotoStmt.Expression is IdentifierNameSyntax { Identifier.ValueText: var labelName } &&
            ReconvergenceSyntax.FindLabel(gotoStmt, labelName) is { } targetLabel)
        {
            var l = JumpRegionPlanner.FindCanonicalStatementIndexForLabel(
                switchStatement.Sections[s].Statements,
                targetLabel,
                canonicalIndices);
            return l >= 0 ? l : -2;
        }

        return -2;
    }

    // The segment of statement statementIndex: the joins of its section placed before it or at its labels
    private static int CountJoinsUpTo(
        ImmutableArray<(int Section, int Position)> nodes,
        ImmutableArray<int> nodesInSection,
        int statementIndex) =>
        nodesInSection
            .AsStackEnumerable()
            .Count((nodes, statementIndex), static (nodeIndex, state) =>
                state.nodes[nodeIndex].Position <= state.statementIndex + 1
            );

    private static bool IsReachable(
        int start,
        int target,
        ImmutableArray<int>[] adjacency,
        bool[] visited,
        Queue<int> queue)
    {
        if (start == target)
        {
            return true;
        }

        Array.Clear(visited, 0, visited.Length);
        queue.Clear();
        visited[start] = true;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in adjacency[current])
            {
                if (next == target)
                {
                    return true;
                }

                if (!visited[next])
                {
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static int[] ComputeTopologicalOrders(
        ImmutableArray<(int Section, int Position)> nodes,
        ImmutableArray<int>[] adjacency,
        out ImmutableArray<SwitchJoin> orderedJoins)
    {
        var joinCount = nodes.Length;
        var inDegree = new int[joinCount];
        for (var u = 0; u < joinCount; u++)
        {
            foreach (var v in adjacency[u])
            {
                inDegree[v]++;
            }
        }

        var done = new bool[joinCount];
        var orderByNode = new int[joinCount];
        var joins = ImmutableArray.CreateBuilder<SwitchJoin>(joinCount);

        for (var step = 1; step <= joinCount; step++)
        {
            var picked = -1;
            for (var u = 0; u < joinCount; u++)
            {
                if (!done[u] && inDegree[u] == 0)
                {
                    picked = u;
                    break;
                }
            }

            if (picked < 0)
            {
                throw new InvalidOperationException("Switch join graph unexpectedly contains a cycle.");
            }

            done[picked] = true;
            orderByNode[picked] = step;
            var (section, position) = nodes[picked];
            joins.Add(new SwitchJoin(step, section, position));

            foreach (var v in adjacency[picked])
            {
                inDegree[v]--;
            }
        }

        orderedJoins = joins.ToImmutable();
        return orderByNode;
    }

    private static ImmutableArray<ImmutableArray<int>> ComputeFirstHeld(
        int sectionCount,
        int joinCount,
        ImmutableArray<int>[] sectionNodes,
        ResolvedJump[] resolvedJumps,
        int[] orderByNode)
    {
        var builder = ImmutableArray.CreateBuilder<ImmutableArray<int>>(sectionCount);
        var defaultOrder = joinCount + 1;

        for (var s = 0; s < sectionCount; s++)
        {
            var segmentCount = sectionNodes[s].Length + 1;
            var mins = new int[segmentCount];
            for (var seg = 0; seg < segmentCount; seg++)
            {
                mins[seg] = seg < sectionNodes[s].Length
                    ? orderByNode[sectionNodes[s][seg]]
                    : defaultOrder;
            }

            foreach (var jump in resolvedJumps)
            {
                if (jump.SourceSection == s)
                {
                    var targetOrder = orderByNode[jump.TargetNode];
                    if (targetOrder < mins[jump.Segment])
                    {
                        mins[jump.Segment] = targetOrder;
                    }
                }
            }

            builder.Add(ImmutableArray.Create(mins));
        }

        return builder.ToImmutable();
    }

    private readonly record struct CollectedJump(
        int SourceSection,
        int StatementIndex,
        GotoStatementSyntax Jump,
        int TargetSection,
        int TargetPosition);

    private readonly record struct ResolvedJump(
        int SourceSection,
        int Segment,
        GotoStatementSyntax Jump,
        int TargetNode);
}
