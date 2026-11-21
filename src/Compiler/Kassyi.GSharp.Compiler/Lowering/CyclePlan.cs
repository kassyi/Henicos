namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// A goto loop as <see cref="CyclePlanner"/> sees it: the statements from which a goto back to its label can be reached
/// again.
/// </summary>
internal sealed record Cycle(
    LabeledStatementSyntax Header,
    SyntaxList<StatementSyntax> Statements,
    JumpRegion Region,
    ImmutableHashSet<FlowNode> Members,
    ImmutableArray<GotoStatementSyntax> BackJumps,
    bool NeedsLatch);

/// <summary>
/// Planned natural-loop cycles for <see cref="ReconvergenceInserter"/>, keyed by the first statement node and last
/// statement index of each <see cref="JumpRegionKind.GotoLoop"/> region.
/// </summary>
internal sealed class CyclePlan
{
    public static readonly CyclePlan Empty =
        new(null, new Dictionary<CycleKey, Cycle>());

    private readonly StatementFlow? _flow;
    private readonly Dictionary<CycleKey, Cycle> _cycles;

    internal CyclePlan(StatementFlow? flow, Dictionary<CycleKey, Cycle> cycles)
    {
        _flow = flow;
        _cycles = cycles;
    }

    public Cycle? Find(SyntaxList<StatementSyntax> statements, JumpRegion region)
    {
        if (_cycles.Count == 0 ||
            region.Kind != JumpRegionKind.GotoLoop ||
            (uint)region.First >= (uint)statements.Count)
        {
            return null;
        }

        return _cycles.TryGetValue(new CycleKey(statements[region.First], region.Last), out var cycle)
            ? cycle
            : null;
    }

    public bool IsLatchStatement(Cycle cycle, StatementSyntax statement) =>
        cycle.Members.Contains(StatementFlow.EntryOf(statement)) &&
        HasBackJumpInside(cycle.BackJumps, statement);

    public bool IsGateStatement(Cycle cycle, StatementSyntax statement) =>
        _flow != null &&
        !cycle.Members.Contains(StatementFlow.EntryOf(statement)) &&
        _flow.TryGetListPosition(statement, out var list, out var index) &&
        index > 0 &&
        IsLatchStatement(cycle, list[index - 1]);

    internal static bool HasBackJumpInside(ImmutableArray<GotoStatementSyntax> backJumps, StatementSyntax statement) =>
        backJumps
            .AsStackEnumerable()
            .Any(statement, static (backJump, enclosing) => ReconvergenceSyntax.IsInside(backJump, enclosing));

    internal readonly record struct CycleKey(StatementSyntax FirstStatement, int Last);
}

/// <summary>
/// Computes natural-loop cycles, latch statements, and gate statements for <see cref="JumpRegionKind.GotoLoop"/> regions.
/// </summary>
internal static class CyclePlanner
{
    public static CyclePlan Plan(
        BlockSyntax body,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Func<ExpressionSyntax, bool?> constantCondition)
    {
        var flow = StatementFlow.Build(body, constantCondition);
        var cycles = new Dictionary<CyclePlan.CycleKey, Cycle>();
        WalkList(body.Statements, flow, holdsSplitPoint, cycles);
        return new CyclePlan(flow, cycles);
    }

    private static void WalkList(
        SyntaxList<StatementSyntax> statements,
        StatementFlow flow,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Dictionary<CyclePlan.CycleKey, Cycle> cycles)
    {
        var plan = JumpRegionPlanner.Plan(statements, holdsSplitPoint);
        if (plan.Rejected.IsEmpty)
        {
            PlanListCycles(statements, plan, flow, cycles);
        }

        foreach (var statement in statements)
        {
            WalkStatement(statement, flow, holdsSplitPoint, cycles);
        }
    }

    private static void WalkStatement(
        StatementSyntax statement,
        StatementFlow flow,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Dictionary<CyclePlan.CycleKey, Cycle> cycles)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        switch (statement)
        {
            case BlockSyntax block:
                WalkList(block.Statements, flow, holdsSplitPoint, cycles);
                break;
            case CheckedStatementSyntax checkedStatement:
                WalkList(checkedStatement.Block.Statements, flow, holdsSplitPoint, cycles);
                break;
            case UnsafeStatementSyntax unsafeStatement:
                WalkList(unsafeStatement.Block.Statements, flow, holdsSplitPoint, cycles);
                break;
            case IfStatementSyntax ifStatement:
                WalkStatement(ifStatement.Statement, flow, holdsSplitPoint, cycles);
                if (ifStatement.Else is { } elseClause)
                {
                    WalkStatement(elseClause.Statement, flow, holdsSplitPoint, cycles);
                }

                break;
            case WhileStatementSyntax whileStatement:
                WalkStatement(whileStatement.Statement, flow, holdsSplitPoint, cycles);
                break;
            case DoStatementSyntax doStatement:
                WalkStatement(doStatement.Statement, flow, holdsSplitPoint, cycles);
                break;
            case ForStatementSyntax forStatement:
                WalkStatement(forStatement.Statement, flow, holdsSplitPoint, cycles);
                break;
            case CommonForEachStatementSyntax forEachStatement:
                WalkStatement(forEachStatement.Statement, flow, holdsSplitPoint, cycles);
                break;
            case SwitchStatementSyntax switchStatement:
                var sectionPlans = new JumpRegionPlan[switchStatement.Sections.Count];
                for (var s = 0; s < switchStatement.Sections.Count; s++)
                {
                    sectionPlans[s] = JumpRegionPlanner.Plan(switchStatement.Sections[s].Statements, holdsSplitPoint);
                }

                var crossSectionRejected =
                    JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, sectionPlans);
                for (var s = 0; s < switchStatement.Sections.Count; s++)
                {
                    var sectionStatements = switchStatement.Sections[s].Statements;
                    if (crossSectionRejected.IsEmpty && sectionPlans[s].Rejected.IsEmpty)
                    {
                        PlanListCycles(sectionStatements, sectionPlans[s], flow, cycles);
                    }

                    foreach (var sectionStatement in sectionStatements)
                    {
                        WalkStatement(sectionStatement, flow, holdsSplitPoint, cycles);
                    }
                }

                break;
        }
    }

    private static void PlanListCycles(
        SyntaxList<StatementSyntax> statements,
        JumpRegionPlan plan,
        StatementFlow flow,
        Dictionary<CyclePlan.CycleKey, Cycle> cycles)
    {
        if (statements.Count == 0 || statements[0].Parent is not { } owner)
        {
            return;
        }

        foreach (var region in plan.Regions)
        {
            if (region.Kind != JumpRegionKind.GotoLoop ||
                (uint)region.First >= (uint)statements.Count ||
                statements[region.First] is not LabeledStatementSyntax header)
            {
                continue;
            }

            var labelGroup = CollectLabelGroup(statements, region.LabelIndex);
            var backJumps = CollectBackJumps(statements, region.First, region.Last, labelGroup);
            if (backJumps.IsEmpty)
            {
                continue;
            }

            var headerNode = StatementFlow.EntryOf(statements[region.First]);
            var members = ComputeMembers(flow, headerNode, statements, region, backJumps);
            if (HasExternalPredecessor(flow, headerNode, members))
            {
                continue;
            }

            members = RestrictToReachableFromHeader(flow, headerNode, members);
            var liveBackJumps = ImmutableArray.CreateBuilder<GotoStatementSyntax>(backJumps.Length);
            backJumps
                .AsStackEnumerable()
                .Where((flow, members), static (jump, state) => state.members.Contains(state.flow.ResolveNode(jump)))
                .AddTo(liveBackJumps);
            if (liveBackJumps.Count == 0)
            {
                continue;
            }

            backJumps = liveBackJumps.ToImmutable();

            if (!HasOnlyAllowedParents(backJumps, members, flow, owner))
            {
                continue;
            }

            if (HasForwardGotoIntoLexicalRange(flow, statements, region, members))
            {
                continue;
            }

            cycles[new CyclePlan.CycleKey(statements[region.First], region.Last)] = new Cycle(
                header,
                statements,
                region,
                members,
                backJumps,
                NeedsLatch: false);
        }
    }

    private static HashSet<LabeledStatementSyntax> CollectLabelGroup(
        SyntaxList<StatementSyntax> statements,
        int labelIndex)
    {
        var labels = new HashSet<LabeledStatementSyntax>();
        for (var i = labelIndex; i < statements.Count; i++)
        {
            for (var cur = statements[i]; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
            {
                labels.Add(labeled);
            }

            if (!JumpRegionPlanner.IsEmptyLabeledStatement(statements[i]))
            {
                break;
            }
        }

        return labels;
    }

    private static ImmutableArray<GotoStatementSyntax> CollectBackJumps(
        SyntaxList<StatementSyntax> statements,
        int first,
        int last,
        HashSet<LabeledStatementSyntax> labelGroup)
    {
        var backJumps = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        for (var k = first; k <= last; k++)
        {
            foreach (var node in statements[k].DescendantNodesAndSelf(static n =>
                         n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            {
                if (node is GotoStatementSyntax gotoStatement &&
                    gotoStatement.IsKind(SyntaxKind.GotoStatement) &&
                    gotoStatement.Expression is IdentifierNameSyntax { Identifier.ValueText: var targetName } &&
                    ReconvergenceSyntax.FindLabel(gotoStatement, targetName) is { } targetLabel &&
                    labelGroup.Contains(targetLabel))
                {
                    backJumps.Add(gotoStatement);
                }
            }
        }

        return backJumps.ToImmutable();
    }

    private static ImmutableHashSet<FlowNode> ComputeMembers(
        StatementFlow flow,
        FlowNode headerNode,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        ImmutableArray<GotoStatementSyntax> backJumps)
    {
        var members = ImmutableHashSet.CreateBuilder<FlowNode>();
        var queue = new Queue<FlowNode>();
        members.Add(headerNode);

        foreach (var backJump in backJumps)
        {
            var jumpNode = flow.ResolveNode(backJump);
            if (IsNodeInLexicalRange(jumpNode, statements, region.First, region.Last) &&
                members.Add(jumpNode))
            {
                queue.Enqueue(jumpNode);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var predecessor in flow.GetPredecessors(current))
            {
                if (IsNodeInLexicalRange(predecessor, statements, region.First, region.Last) &&
                    members.Add(predecessor))
                {
                    queue.Enqueue(predecessor);
                }
            }
        }

        return members.ToImmutable();
    }

    private static ImmutableHashSet<FlowNode> RestrictToReachableFromHeader(
        StatementFlow flow,
        FlowNode headerNode,
        ImmutableHashSet<FlowNode> backwardMembers)
    {
        var reachable = ImmutableHashSet.CreateBuilder<FlowNode>();
        var queue = new Queue<FlowNode>();
        reachable.Add(headerNode);
        queue.Enqueue(headerNode);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var successor in flow.GetSuccessors(current))
            {
                if (backwardMembers.Contains(successor) && reachable.Add(successor))
                {
                    queue.Enqueue(successor);
                }
            }
        }

        return reachable.ToImmutable();
    }

    // No non-header member has an incoming edge from outside the natural loop
    private static bool HasExternalPredecessor(
        StatementFlow flow,
        FlowNode headerNode,
        ImmutableHashSet<FlowNode> members)
    {
        foreach (var member in members)
        {
            if (member == headerNode)
            {
                continue;
            }

            foreach (var predecessor in flow.GetPredecessors(member))
            {
                if (!members.Contains(predecessor))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Every back jump and gate statement reaches the loop's statement list through transparent parents only
    private static bool HasOnlyAllowedParents(
        ImmutableArray<GotoStatementSyntax> backJumps,
        ImmutableHashSet<FlowNode> members,
        StatementFlow flow,
        SyntaxNode owner)
    {
        foreach (var backJump in backJumps)
        {
            if (!HasAllowedAncestors(backJump, owner))
            {
                return false;
            }
        }

        foreach (var member in members)
        {
            if (member.Kind != FlowNodeKind.Statement ||
                member.Syntax is not StatementSyntax memberStatement ||
                !flow.TryGetListPosition(memberStatement, out var list, out var index) ||
                index + 1 >= list.Count ||
                !CyclePlan.HasBackJumpInside(backJumps, memberStatement))
            {
                continue;
            }

            var candidateGate = list[index + 1];
            if (!members.Contains(StatementFlow.EntryOf(candidateGate)) &&
                !HasAllowedAncestors(candidateGate, owner))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAllowedAncestors(SyntaxNode node, SyntaxNode owner)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current == owner)
            {
                return true;
            }

            if (current is not (BlockSyntax or LabeledStatementSyntax or CheckedStatementSyntax
                or UnsafeStatementSyntax or IfStatementSyntax or ElseClauseSyntax))
            {
                return false;
            }
        }

        return false;
    }

    // No goto reachable from the natural loop inside statements[First..Last] jumps to a non-member label inside
    // statements[First..Last]
    private static bool HasForwardGotoIntoLexicalRange(
        StatementFlow flow,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        ImmutableHashSet<FlowNode> members)
    {
        var visited = new HashSet<FlowNode>();
        var queue = new Queue<FlowNode>();

        foreach (var member in members)
        {
            if (visited.Add(member))
            {
                queue.Enqueue(member);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Kind == FlowNodeKind.Statement &&
                current.Syntax is StatementSyntax currentStatement &&
                JumpsToLabelOutsideMembersInRange(currentStatement, flow, statements, region, members))
            {
                return true;
            }

            foreach (var successor in flow.GetSuccessors(current))
            {
                if (successor.Kind is not (FlowNodeKind.Statement or FlowNodeKind.LoopTest) ||
                    successor.Syntax is not StatementSyntax successorStatement ||
                    !IsInLexicalRange(successorStatement, statements, region.First, region.Last))
                {
                    continue;
                }

                if (visited.Add(successor))
                {
                    queue.Enqueue(successor);
                }
            }
        }

        return false;
    }

    private static bool JumpsToLabelOutsideMembersInRange(
        StatementSyntax statement,
        StatementFlow flow,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        ImmutableHashSet<FlowNode> members)
    {
        var inner = statement;
        while (inner is LabeledStatementSyntax labeled)
        {
            inner = labeled.Statement;
        }

        if (inner is GotoStatementSyntax directGoto)
        {
            return IsForwardGotoIntoLexicalRange(directGoto, flow, statements, region, members);
        }

        if (inner is BlockSyntax or CheckedStatementSyntax or UnsafeStatementSyntax or IfStatementSyntax
            or SwitchStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
            or CommonForEachStatementSyntax)
        {
            return false;
        }

        foreach (var node in inner.DescendantNodesAndSelf(static n =>
                     n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is GotoStatementSyntax gotoStatement &&
                IsForwardGotoIntoLexicalRange(gotoStatement, flow, statements, region, members))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForwardGotoIntoLexicalRange(
        GotoStatementSyntax gotoStatement,
        StatementFlow flow,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        ImmutableHashSet<FlowNode> members)
    {
        if (!gotoStatement.IsKind(SyntaxKind.GotoStatement) ||
            gotoStatement.Expression is not IdentifierNameSyntax { Identifier.ValueText: var labelName } ||
            ReconvergenceSyntax.FindLabel(gotoStatement, labelName) is not { } targetLabel)
        {
            return false;
        }

        return !members.Contains(flow.ResolveNode(targetLabel)) &&
               IsInLexicalRange(targetLabel, statements, region.First, region.Last);
    }

    private static bool IsNodeInLexicalRange(
        FlowNode node,
        SyntaxList<StatementSyntax> statements,
        int first,
        int last) =>
        node.Syntax is StatementSyntax statement &&
        IsInLexicalRange(statement, statements, first, last);

    private static bool IsInLexicalRange(
        StatementSyntax statement,
        SyntaxList<StatementSyntax> statements,
        int first,
        int last)
    {
        for (var i = first; i <= last; i++)
        {
            if (ReconvergenceSyntax.IsInside(statement, statements[i]))
            {
                return true;
            }
        }

        return false;
    }
}
