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
    bool NeedsLatch,
    ImmutableHashSet<StatementSyntax>? BranchGates = null);

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
        (IsSerialGate(cycle, _flow, statement) || (cycle.BranchGates?.Contains(statement) ?? false));

    // The statement right after a latch statement in the same list
    private bool IsSerialGate(Cycle cycle, StatementFlow flow, StatementSyntax statement) =>
        flow.TryGetListPosition(statement, out var list, out var index) &&
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
        WalkList(body.Statements, flow, holdsSplitPoint, constantCondition, cycles);
        return new CyclePlan(flow, cycles);
    }

    private static void WalkList(
        SyntaxList<StatementSyntax> statements,
        StatementFlow flow,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Func<ExpressionSyntax, bool?> constantCondition,
        Dictionary<CyclePlan.CycleKey, Cycle> cycles)
    {
        var plan = JumpRegionPlanner.Plan(statements, holdsSplitPoint);
        if (plan.Rejected.IsEmpty)
        {
            PlanListCycles(statements, plan, flow, constantCondition, cycles);
        }

        foreach (var statement in statements)
        {
            WalkStatement(statement, flow, holdsSplitPoint, constantCondition, cycles);
        }
    }

    private static void WalkStatement(
        StatementSyntax statement,
        StatementFlow flow,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Func<ExpressionSyntax, bool?> constantCondition,
        Dictionary<CyclePlan.CycleKey, Cycle> cycles)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        switch (statement)
        {
            case BlockSyntax block:
                WalkList(block.Statements, flow, holdsSplitPoint, constantCondition, cycles);
                break;
            case CheckedStatementSyntax checkedStatement:
                WalkList(checkedStatement.Block.Statements, flow, holdsSplitPoint, constantCondition, cycles);
                break;
            case UnsafeStatementSyntax unsafeStatement:
                WalkList(unsafeStatement.Block.Statements, flow, holdsSplitPoint, constantCondition, cycles);
                break;
            case IfStatementSyntax ifStatement:
                WalkStatement(ifStatement.Statement, flow, holdsSplitPoint, constantCondition, cycles);
                if (ifStatement.Else is { } elseClause)
                {
                    WalkStatement(elseClause.Statement, flow, holdsSplitPoint, constantCondition, cycles);
                }

                break;
            case WhileStatementSyntax whileStatement:
                WalkStatement(whileStatement.Statement, flow, holdsSplitPoint, constantCondition, cycles);
                break;
            case DoStatementSyntax doStatement:
                WalkStatement(doStatement.Statement, flow, holdsSplitPoint, constantCondition, cycles);
                break;
            case ForStatementSyntax forStatement:
                WalkStatement(forStatement.Statement, flow, holdsSplitPoint, constantCondition, cycles);
                break;
            case CommonForEachStatementSyntax forEachStatement:
                WalkStatement(forEachStatement.Statement, flow, holdsSplitPoint, constantCondition, cycles);
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
                        PlanListCycles(sectionStatements, sectionPlans[s], flow, constantCondition, cycles);
                    }

                    foreach (var sectionStatement in sectionStatements)
                    {
                        WalkStatement(sectionStatement, flow, holdsSplitPoint, constantCondition, cycles);
                    }
                }

                break;
        }
    }

    private static void PlanListCycles(
        SyntaxList<StatementSyntax> statements,
        JumpRegionPlan plan,
        StatementFlow flow,
        Func<ExpressionSyntax, bool?> constantCondition,
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
            var headerNodes = CollectHeaderNodes(statements, region.First);
            var members = ComputeMembers(flow, headerNode, statements, region, backJumps);
            if (HasExternalPredecessor(flow, headerNodes, members))
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

            var branchGates = FindBranchGates(members, backJumps, constantCondition);
            if (!HasOnlyAllowedParents(backJumps, members, flow, owner) ||
                !HasAllowedAncestorsForAll(branchGates, owner))
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
                NeedsLatch: backJumps.Length >= 2,
                BranchGates: branchGates);
        }
    }

    // The first statement of one branch of a latch if whose other branch only goes back: lanes leave the loop there
    private static ImmutableHashSet<StatementSyntax> FindBranchGates(
        ImmutableHashSet<FlowNode> members,
        ImmutableArray<GotoStatementSyntax> backJumps,
        Func<ExpressionSyntax, bool?> constantCondition)
    {
        var gates = ImmutableHashSet.CreateBuilder<StatementSyntax>();
        foreach (var member in members)
        {
            if (member.Kind != FlowNodeKind.Statement ||
                member.Syntax is not IfStatementSyntax { Else: { } elseClause } ifStatement ||
                !CyclePlan.HasBackJumpInside(backJumps, ifStatement))
            {
                continue;
            }

            if (IsBackOnlyBranch(ifStatement.Statement, members, constantCondition) &&
                FirstStatementOf(elseClause.Statement) is { } elseFirst)
            {
                gates.Add(elseFirst);
            }

            if (IsBackOnlyBranch(elseClause.Statement, members, constantCondition) &&
                FirstStatementOf(ifStatement.Statement) is { } thenFirst)
            {
                gates.Add(thenFirst);
            }
        }

        return gates.ToImmutable();
    }

    private static StatementSyntax? FirstStatementOf(StatementSyntax branch) =>
        branch is BlockSyntax block ? (block.Statements.Count == 0 ? null : block.Statements[0]) : branch;

    // A branch that is not empty, cannot fall through, and whose statements are all in the loop
    private static bool IsBackOnlyBranch(
        StatementSyntax branch,
        ImmutableHashSet<FlowNode> members,
        Func<ExpressionSyntax, bool?> constantCondition)
    {
        if (branch is BlockSyntax { Statements.Count: 0 } || CanFallThrough(branch, constantCondition))
        {
            return false;
        }

        return branch
            .DescendantNodesAndSelf(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .OfType<StatementSyntax>()
            .All(members, static (statement, set) => set.Contains(StatementFlow.EntryOf(statement)));
    }

    // Whether control can reach the end of the statement; a form that is not sure answers true (no gate is placed)
    private static bool CanFallThrough(StatementSyntax statement, Func<ExpressionSyntax, bool?> constantCondition)
    {
        switch (statement)
        {
            case GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax or ReturnStatementSyntax
                or ThrowStatementSyntax:
                return false;
            case BlockSyntax block:
                return block.Statements.Count == 0 || CanFallThrough(block.Statements[^1], constantCondition);
            case CheckedStatementSyntax checkedStatement:
                return CanFallThrough(checkedStatement.Block, constantCondition);
            case UnsafeStatementSyntax unsafeStatement:
                return CanFallThrough(unsafeStatement.Block, constantCondition);
            case LabeledStatementSyntax labeled:
                return CanFallThrough(labeled.Statement, constantCondition);
            case IfStatementSyntax ifStatement:
                return ifStatement.Else == null ||
                       CanFallThrough(ifStatement.Statement, constantCondition) ||
                       CanFallThrough(ifStatement.Else.Statement, constantCondition);
            case WhileStatementSyntax whileStatement:
                return constantCondition(whileStatement.Condition) != true || HasOwnBreak(whileStatement.Statement);
            case DoStatementSyntax doStatement:
                return constantCondition(doStatement.Condition) != true || HasOwnBreak(doStatement.Statement);
            case ForStatementSyntax forStatement:
                return (forStatement.Condition != null && constantCondition(forStatement.Condition) != true) ||
                       HasOwnBreak(forStatement.Statement);
            default:
                return true;
        }
    }

    // A break of the loop itself, not of a loop or switch inside its body
    private static bool HasOwnBreak(StatementSyntax body) =>
        body
            .DescendantNodesAndSelf(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax
                or SwitchStatementSyntax))
            .AsStackEnumerable()
            .OfType<BreakStatementSyntax>()
            .Any();

    private static bool HasAllowedAncestorsForAll(ImmutableHashSet<StatementSyntax> statements, SyntaxNode owner)
    {
        foreach (var statement in statements)
        {
            if (!HasAllowedAncestors(statement, owner))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<FlowNode> CollectHeaderNodes(
        SyntaxList<StatementSyntax> statements,
        int first)
    {
        var headerNodes = new HashSet<FlowNode>
        {
            StatementFlow.EntryOf(statements[first])
        };
        var i = first;
        while (i + 1 < statements.Count &&
               JumpRegionPlanner.IsEmptyLabeledStatement(statements[i]) &&
               statements[i + 1] is LabeledStatementSyntax)
        {
            i++;
            headerNodes.Add(StatementFlow.EntryOf(statements[i]));
        }

        return headerNodes;
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
        HashSet<FlowNode> headerNodes,
        ImmutableHashSet<FlowNode> members)
    {
        foreach (var member in members)
        {
            if (headerNodes.Contains(member))
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
