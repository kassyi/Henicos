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
        new(null, new Dictionary<CycleKey, Cycle>(), ImmutableArray<Cycle>.Empty);

    private readonly StatementFlow? _flow;
    private readonly Dictionary<CycleKey, Cycle> _cycles;
    private readonly ImmutableArray<Cycle> _orderedCycles;

    internal CyclePlan(
        StatementFlow? flow,
        Dictionary<CycleKey, Cycle> cycles,
        ImmutableArray<Cycle> orderedCycles)
    {
        _flow = flow;
        _cycles = cycles;
        _orderedCycles = orderedCycles;
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
        (IsSerialGate(cycle, _flow, statement) ||
         (!cycle.Members.Contains(StatementFlow.EntryOf(statement)) &&
          (cycle.BranchGates?.Contains(statement) ?? false)));

    // The statement right after a member statement of the same list that falls out of the cycle
    private static bool IsSerialGate(Cycle cycle, StatementFlow flow, StatementSyntax statement) =>
        flow.TryGetListPosition(statement, out var list, out var index) &&
        index > 0 &&
        IsGateTransition(flow, cycle.Members, list[index - 1], statement);

    internal static bool IsGateTransition(
        StatementFlow flow,
        ImmutableHashSet<FlowNode> members,
        StatementSyntax prev,
        StatementSyntax candidateGate) =>
        members.Contains(StatementFlow.EntryOf(prev)) &&
        !members.Contains(StatementFlow.EntryOf(candidateGate)) &&
        flow.ReachesEnd(prev);

    public ImmutableArray<Cycle> FindCyclesGatedInside(StatementSyntax loop)
    {
        if (_flow == null || _orderedCycles.IsDefaultOrEmpty)
        {
            return ImmutableArray<Cycle>.Empty;
        }

        var body = CyclePlanner.GetLoopBody(loop);
        var builder = ImmutableArray.CreateBuilder<Cycle>();
        _orderedCycles
            .AsStackEnumerable()
            .Where((Self: this, Loop: loop, Body: body), static (cycle, state) =>
                state.Self.DoesLoopExitCycle(state.Loop, state.Body, cycle) &&
                state.Self.HasGateStatementInside(cycle, state.Body)
            ).AddTo(builder);
        return builder.ToImmutable();
    }

    public bool HasNaturalLoop(StatementSyntax loop) =>
        _flow != null && CyclePlanner.HasNaturalLoop(_flow, loop);

    private bool DoesLoopExitCycle(StatementSyntax loop, StatementSyntax body, Cycle cycle) =>
        _flow != null &&
        !ReconvergenceSyntax.IsInside(cycle.Header, body) &&
        _flow.TryGetBreakAfter(loop, out var after) &&
        !cycle.Members.Contains(after);

    private bool HasGateStatementInside(Cycle cycle, StatementSyntax body)
    {
        if (_flow == null)
        {
            return false;
        }

        foreach (var member in cycle.Members)
        {
            if (member.Kind != FlowNodeKind.Statement ||
                member.Syntax is not StatementSyntax memberStatement ||
                !_flow.TryGetListPosition(memberStatement, out var list, out var index) ||
                index + 1 >= list.Count)
            {
                continue;
            }

            var candidateGate = list[index + 1];
            if (IsGateTransition(_flow, cycle.Members, memberStatement, candidateGate) &&
                ReconvergenceSyntax.IsInside(candidateGate, body))
            {
                return true;
            }
        }

        return false;
    }

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
        var listPlans = ImmutableArray.CreateBuilder<(SyntaxList<StatementSyntax> Statements, JumpRegionPlan Plan)>();
        var allGotoLoops = ImmutableArray.CreateBuilder<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)>();
        CollectListPlans(body.Statements, holdsSplitPoint, listPlans, allGotoLoops);

        var cycles = new Dictionary<CyclePlan.CycleKey, Cycle>();
        var orderedCycles = ImmutableArray.CreateBuilder<Cycle>();
        var gotoLoops = allGotoLoops.ToImmutable();
        foreach (var (statements, plan) in listPlans)
        {
            PlanListCycles(statements, plan, flow, gotoLoops, holdsSplitPoint, constantCondition, cycles, orderedCycles);
        }

        return new CyclePlan(flow, cycles, orderedCycles.ToImmutable());
    }

    internal static StatementSyntax GetLoopBody(StatementSyntax loop) =>
        loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            CommonForEachStatementSyntax forEach => forEach.Statement,
            _ => throw new InvalidOperationException($"Not a loop: {loop.Kind()}")
        };

    internal static bool HasNaturalLoop(StatementFlow flow, StatementSyntax loop)
    {
        var body = GetLoopBody(loop);
        var bodyEntry = StatementFlow.EntryOf(body);
        var loopTarget = loop is DoStatementSyntax
            ? new FlowNode(loop, FlowNodeKind.LoopTest)
            : flow.ResolveNode(loop);

        if (!flow.GetSuccessors(loopTarget).Contains(bodyEntry))
        {
            return false;
        }

        var visited = new HashSet<FlowNode> { bodyEntry };
        var queue = new Queue<FlowNode>();
        queue.Enqueue(bodyEntry);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var successor in flow.GetSuccessors(current))
            {
                if (successor == loopTarget)
                {
                    return true;
                }

                if (successor.Syntax is { } syntax &&
                    ReconvergenceSyntax.IsInside(syntax, body) &&
                    visited.Add(successor))
                {
                    queue.Enqueue(successor);
                }
            }
        }

        return false;
    }

    private static bool OwnsBreakOrContinue(StatementSyntax loop) =>
        GetLoopBody(loop)
            .DescendantNodesAndSelf(static n =>
                n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .Any(loop, static (node, target) =>
                (node is BreakStatementSyntax &&
                 ReconvergenceSyntax.FindJumpTarget(node, loopOnly: false) == target) ||
                (node is ContinueStatementSyntax &&
                 ReconvergenceSyntax.FindJumpTarget(node, loopOnly: true) == target));

    private static void CollectListPlans(
        SyntaxList<StatementSyntax> statements,
        Func<StatementSyntax, bool> holdsSplitPoint,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegionPlan Plan)>.Builder listPlans,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)>.Builder allGotoLoops)
    {
        var plan = JumpRegionPlanner.Plan(statements, holdsSplitPoint);
        if (plan.Rejected.IsEmpty)
        {
            listPlans.Add((statements, plan));
            foreach (var region in plan.Regions)
            {
                if (region.Kind == JumpRegionKind.GotoLoop)
                {
                    allGotoLoops.Add((statements, region));
                }
            }
        }

        foreach (var statement in statements)
        {
            CollectStatementPlans(statement, holdsSplitPoint, listPlans, allGotoLoops);
        }
    }

    private static void CollectStatementPlans(
        StatementSyntax statement,
        Func<StatementSyntax, bool> holdsSplitPoint,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegionPlan Plan)>.Builder listPlans,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)>.Builder allGotoLoops)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        switch (statement)
        {
            case BlockSyntax block:
                CollectListPlans(block.Statements, holdsSplitPoint, listPlans, allGotoLoops);
                break;
            case CheckedStatementSyntax checkedStatement:
                CollectListPlans(checkedStatement.Block.Statements, holdsSplitPoint, listPlans, allGotoLoops);
                break;
            case UnsafeStatementSyntax unsafeStatement:
                CollectListPlans(unsafeStatement.Block.Statements, holdsSplitPoint, listPlans, allGotoLoops);
                break;
            case IfStatementSyntax ifStatement:
                CollectStatementPlans(ifStatement.Statement, holdsSplitPoint, listPlans, allGotoLoops);
                if (ifStatement.Else is { } elseClause)
                {
                    CollectStatementPlans(elseClause.Statement, holdsSplitPoint, listPlans, allGotoLoops);
                }

                break;
            case WhileStatementSyntax whileStatement:
                CollectStatementPlans(whileStatement.Statement, holdsSplitPoint, listPlans, allGotoLoops);
                break;
            case DoStatementSyntax doStatement:
                CollectStatementPlans(doStatement.Statement, holdsSplitPoint, listPlans, allGotoLoops);
                break;
            case ForStatementSyntax forStatement:
                CollectStatementPlans(forStatement.Statement, holdsSplitPoint, listPlans, allGotoLoops);
                break;
            case CommonForEachStatementSyntax forEachStatement:
                CollectStatementPlans(forEachStatement.Statement, holdsSplitPoint, listPlans, allGotoLoops);
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
                        listPlans.Add((sectionStatements, sectionPlans[s]));
                        foreach (var region in sectionPlans[s].Regions)
                        {
                            if (region.Kind == JumpRegionKind.GotoLoop)
                            {
                                allGotoLoops.Add((sectionStatements, region));
                            }
                        }
                    }

                    foreach (var sectionStatement in sectionStatements)
                    {
                        CollectStatementPlans(sectionStatement, holdsSplitPoint, listPlans, allGotoLoops);
                    }
                }

                break;
        }
    }

    private static void PlanListCycles(
        SyntaxList<StatementSyntax> statements,
        JumpRegionPlan plan,
        StatementFlow flow,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)> allGotoLoops,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Func<ExpressionSyntax, bool?> constantCondition,
        Dictionary<CyclePlan.CycleKey, Cycle> cycles,
        ImmutableArray<Cycle>.Builder orderedCycles)
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
                !AreBranchGatesAllowed(branchGates, members, flow, owner))
            {
                continue;
            }

            if (HasForwardGotoIntoLexicalRange(flow, statements, region, members))
            {
                continue;
            }

            var needsLatch = backJumps.Length >= 2 ||
                NeedsLatchForSingleBackJump(backJumps[0], owner, statements, region, members, flow, allGotoLoops) ||
                HasUngatedSplit(statements, region, flow, members, branchGates, holdsSplitPoint);
            var cycle = new Cycle(
                header,
                statements,
                region,
                members,
                backJumps,
                needsLatch,
                branchGates);
            cycles[new CyclePlan.CycleKey(statements[region.First], region.Last)] = cycle;
            orderedCycles.Add(cycle);
        }
    }

    private static bool AreBranchGatesAllowed(
        ImmutableHashSet<StatementSyntax> branchGates,
        ImmutableHashSet<FlowNode> members,
        StatementFlow flow,
        SyntaxNode owner)
    {
        foreach (var gate in branchGates)
        {
            if (!HasAllowedAncestors(gate, owner) || IsInsideNonNaturalLoopWithJump(gate, owner, flow, members))
            {
                return false;
            }
        }

        return true;
    }

    // (C3) A statement of the loop's range that is outside the cycle, has not passed a gate and holds a split point:
    // the lanes that fall out of the loop there have left it in different passes
    private static bool HasUngatedSplit(
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        StatementFlow flow,
        ImmutableHashSet<FlowNode> members,
        ImmutableHashSet<StatementSyntax> branchGates,
        Func<StatementSyntax, bool> holdsSplitPoint)
    {
        var scan = new UngatedScan(flow, members, branchGates, holdsSplitPoint);
        return scan.InList(statements, region.First, region.Last);
    }

    private readonly record struct UngatedScan(
        StatementFlow Flow,
        ImmutableHashSet<FlowNode> Members,
        ImmutableHashSet<StatementSyntax> BranchGates,
        Func<StatementSyntax, bool> HoldsSplitPoint)
    {
        // The statements after a gate statement of the list, and the gate statement itself, are past the gate
        public bool InList(SyntaxList<StatementSyntax> list, int first, int last)
        {
            for (var i = first; i <= last; i++)
            {
                var statement = list[i];
                if (BranchGates.Contains(statement))
                {
                    return false;
                }

                if (i > 0 && CyclePlan.IsGateTransition(Flow, Members, list[i - 1], statement))
                {
                    return false;
                }

                if (InStatement(statement))
                {
                    return true;
                }
            }

            return false;
        }

        // asMember: the flow graph has one node for a chain of labels and its statement, so the statement of a label of
        // the cycle is a member too
        private bool InStatement(StatementSyntax statement, bool asMember = false)
        {
            if (BranchGates.Contains(statement))
            {
                return false;
            }

            switch (statement)
            {
                case BlockSyntax block:
                    return InList(block.Statements, 0, block.Statements.Count - 1);
                case CheckedStatementSyntax checkedStatement:
                    return InStatement(checkedStatement.Block);
                case UnsafeStatementSyntax unsafeStatement:
                    return InStatement(unsafeStatement.Block);
                case LabeledStatementSyntax labeled:
                    return InStatement(labeled.Statement, asMember || Members.Contains(StatementFlow.EntryOf(labeled)));
            }

            if (!asMember && !Members.Contains(StatementFlow.EntryOf(statement)) && HoldsSplitPoint(statement))
            {
                return true;
            }

            switch (statement)
            {
                case IfStatementSyntax ifStatement:
                    return InStatement(ifStatement.Statement) ||
                           (ifStatement.Else is { } elseClause && InStatement(elseClause.Statement));
                case WhileStatementSyntax whileStatement:
                    return InStatement(whileStatement.Statement);
                case DoStatementSyntax doStatement:
                    return InStatement(doStatement.Statement);
                case ForStatementSyntax forStatement:
                    return InStatement(forStatement.Statement);
                case CommonForEachStatementSyntax forEach:
                    return InStatement(forEach.Statement);
                case SwitchStatementSyntax switchStatement:
                    foreach (var section in switchStatement.Sections)
                    {
                        if (InList(section.Statements, 0, section.Statements.Count - 1))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
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

    private static bool NeedsLatchForSingleBackJump(
        GotoStatementSyntax backJump,
        SyntaxNode owner,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        ImmutableHashSet<FlowNode> members,
        StatementFlow flow,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)> allGotoLoops)
    {
        for (var current = backJump.Parent; current != null && current != owner; current = current.Parent)
        {
            if (current is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax &&
                members.Contains(flow.ResolveNode(current)) &&
                HasNaturalLoop(flow, (StatementSyntax)current))
            {
                return true;
            }
        }

        foreach (var (otherStatements, otherRegion) in allGotoLoops)
        {
            if (ReferenceEquals(otherRegion, region) ||
                (uint)otherRegion.First >= (uint)otherStatements.Count ||
                (uint)otherRegion.Last >= (uint)otherStatements.Count)
            {
                continue;
            }

            if (IsInLexicalRange(otherStatements[otherRegion.First], statements, region.First, region.Last) &&
                IsInLexicalRange(backJump, otherStatements, otherRegion.First, otherRegion.Last))
            {
                return true;
            }
        }

        return false;
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
                index + 1 >= list.Count)
            {
                continue;
            }

            var candidateGate = list[index + 1];
            if (CyclePlan.IsGateTransition(flow, members, memberStatement, candidateGate) &&
                (!HasAllowedAncestors(candidateGate, owner) ||
                 IsInsideNonNaturalLoopWithJump(candidateGate, owner, flow, members)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInsideNonNaturalLoopWithJump(
        SyntaxNode node,
        SyntaxNode owner,
        StatementFlow flow,
        ImmutableHashSet<FlowNode> members)
    {
        for (var current = node.Parent; current != null && current != owner; current = current.Parent)
        {
            if (current is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax &&
                flow.TryGetBreakAfter(current, out var after) &&
                !members.Contains(after) &&
                !HasNaturalLoop(flow, (StatementSyntax)current) &&
                OwnsBreakOrContinue((StatementSyntax)current))
            {
                return true;
            }
        }

        return false;
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
                or UnsafeStatementSyntax or IfStatementSyntax or ElseClauseSyntax
                or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax))
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
