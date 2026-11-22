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
/// A structured loop (<c>for</c>, <c>while</c>, <c>do</c>, or <c>foreach</c>) that has at least one gate statement
/// right after a statement in its natural loop holding one of its own <c>continue</c> statements.
/// </summary>
internal sealed record LoopCycle(
    StatementSyntax Loop,
    ImmutableHashSet<FlowNode> Members,
    ImmutableArray<ContinueStatementSyntax> BackJumps,
    ImmutableHashSet<StatementSyntax> Gates);

/// <summary>
/// Planned natural-loop cycles for <see cref="ReconvergenceInserter"/>, keyed by the first statement node and last
/// statement index of each <see cref="JumpRegionKind.GotoLoop"/> region and by each planned structured loop statement.
/// </summary>
internal sealed class CyclePlan
{
    public static readonly CyclePlan Empty =
        new(
            null,
            new Dictionary<CycleKey, Cycle>(),
            ImmutableArray<Cycle>.Empty,
            new Dictionary<StatementSyntax, LoopCycle>());

    private readonly StatementFlow? _flow;
    private readonly Dictionary<CycleKey, Cycle> _cycles;
    private readonly ImmutableArray<Cycle> _orderedCycles;
    private readonly Dictionary<StatementSyntax, LoopCycle> _loopCycles;

    internal CyclePlan(
        StatementFlow? flow,
        Dictionary<CycleKey, Cycle> cycles,
        ImmutableArray<Cycle> orderedCycles,
        Dictionary<StatementSyntax, LoopCycle> loopCycles)
    {
        _flow = flow;
        _cycles = cycles;
        _orderedCycles = orderedCycles;
        _loopCycles = loopCycles;
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

    public LoopCycle? FindLoop(StatementSyntax loop) =>
        _loopCycles.TryGetValue(loop, out var cycle) ? cycle : null;

    public bool IsLatchStatement(Cycle cycle, StatementSyntax statement) =>
        cycle.Members.Contains(StatementFlow.EntryOf(statement)) &&
        HasBackJumpInside(cycle.BackJumps, statement);

    public bool IsGateStatement(Cycle cycle, StatementSyntax statement) =>
        _flow != null &&
        _flow.TryGetListPosition(statement, out var list, out var index) &&
        index > 0 &&
        IsGateTransition(_flow, cycle.Members, list[index - 1], statement);

    public bool IsGateStatement(LoopCycle cycle, StatementSyntax statement) =>
        cycle.Gates.Contains(statement);

    internal static bool IsGateTransition(
        StatementFlow flow,
        ImmutableHashSet<FlowNode> members,
        StatementSyntax prev,
        StatementSyntax candidateGate) =>
        members.Contains(StatementFlow.EntryOf(prev)) &&
        !members.Contains(StatementFlow.EntryOf(candidateGate)) &&
        flow.ReachesEnd(prev);

    public ImmutableArray<Cycle> FindCyclesGatedInside(StatementSyntax statement)
    {
        if (_flow == null || _orderedCycles.IsDefaultOrEmpty)
        {
            return ImmutableArray<Cycle>.Empty;
        }

        SyntaxNode extent = statement is SwitchStatementSyntax
            ? statement
            : CyclePlanner.GetLoopBody(statement);
        var builder = ImmutableArray.CreateBuilder<Cycle>();
        _orderedCycles
            .AsStackEnumerable()
            .Where((Self: this, Statement: statement, Extent: extent), static (cycle, state) =>
                state.Self.DoesLoopExitCycle(state.Statement, state.Extent, cycle) &&
                state.Self.HasGateStatementInside(cycle, state.Extent)
            ).AddTo(builder);
        return builder.ToImmutable();
    }

    public bool HasNaturalLoop(StatementSyntax loop) =>
        _flow != null && CyclePlanner.HasNaturalLoop(_flow, loop);

    private bool DoesLoopExitCycle(StatementSyntax statement, SyntaxNode extent, Cycle cycle) =>
        _flow != null &&
        !ReconvergenceSyntax.IsInside(cycle.Header, extent) &&
        _flow.TryGetBreakAfter(statement, out var after) &&
        !cycle.Members.Contains(after);

    private bool HasGateStatementInside(Cycle cycle, SyntaxNode extent)
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
                ReconvergenceSyntax.IsInside(candidateGate, extent))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasBackJumpInside<TJump>(ImmutableArray<TJump> backJumps, StatementSyntax statement)
        where TJump : SyntaxNode =>
        backJumps
            .AsStackEnumerable()
            .Any(statement, static (backJump, enclosing) => ReconvergenceSyntax.IsInside(backJump, enclosing));

    internal readonly record struct CycleKey(StatementSyntax FirstStatement, int Last);
}

/// <summary>
/// Computes natural-loop cycles, latch statements, and gate statements for <see cref="JumpRegionKind.GotoLoop"/> regions
/// and structured loops (<c>for</c>, <c>while</c>, <c>do</c>, <c>foreach</c>).
/// </summary>
internal static class CyclePlanner
{
    public static CyclePlan Plan(
        BlockSyntax body,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Func<ExpressionSyntax, bool?> constantCondition,
        SemanticModel? semanticModel = null)
    {
        var flow = StatementFlow.Build(body, constantCondition);
        var listPlans = ImmutableArray.CreateBuilder<(SyntaxList<StatementSyntax> Statements, JumpRegionPlan Plan)>();
        var allGotoLoops = ImmutableArray.CreateBuilder<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)>();
        var allLoops = ImmutableArray.CreateBuilder<StatementSyntax>();
        var switchesWithJoins = new HashSet<SwitchStatementSyntax>();
        CollectListPlans(
            body.Statements,
            holdsSplitPoint,
            semanticModel,
            listPlans,
            allGotoLoops,
            allLoops,
            switchesWithJoins);

        var cycles = new Dictionary<CyclePlan.CycleKey, Cycle>();
        var orderedCycles = ImmutableArray.CreateBuilder<Cycle>();
        var gotoLoops = allGotoLoops.ToImmutable();
        foreach (var (statements, plan) in listPlans)
        {
            PlanListCycles(
                statements, plan, flow, semanticModel, gotoLoops, switchesWithJoins, cycles, orderedCycles);
        }

        var loopCycles = new Dictionary<StatementSyntax, LoopCycle>();
        PlanLoopCycles(allLoops.ToImmutable(), flow, holdsSplitPoint, loopCycles);

        return new CyclePlan(flow, cycles, orderedCycles.ToImmutable(), loopCycles);
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
        SemanticModel? semanticModel,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegionPlan Plan)>.Builder listPlans,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)>.Builder allGotoLoops,
        ImmutableArray<StatementSyntax>.Builder allLoops,
        HashSet<SwitchStatementSyntax> switchesWithJoins)
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
            CollectStatementPlans(
                statement, holdsSplitPoint, semanticModel, listPlans, allGotoLoops, allLoops, switchesWithJoins);
        }
    }

    private static void CollectStatementPlans(
        StatementSyntax statement,
        Func<StatementSyntax, bool> holdsSplitPoint,
        SemanticModel? semanticModel,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegionPlan Plan)>.Builder listPlans,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)>.Builder allGotoLoops,
        ImmutableArray<StatementSyntax>.Builder allLoops,
        HashSet<SwitchStatementSyntax> switchesWithJoins)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        switch (statement)
        {
            case BlockSyntax block:
                CollectListPlans(
                    block.Statements, holdsSplitPoint, semanticModel, listPlans, allGotoLoops, allLoops, switchesWithJoins);
                break;
            case CheckedStatementSyntax checkedStatement:
                CollectListPlans(
                    checkedStatement.Block.Statements,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                break;
            case UnsafeStatementSyntax unsafeStatement:
                CollectListPlans(
                    unsafeStatement.Block.Statements,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                break;
            case IfStatementSyntax ifStatement:
                CollectStatementPlans(
                    ifStatement.Statement,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                if (ifStatement.Else is { } elseClause)
                {
                    CollectStatementPlans(
                        elseClause.Statement,
                        holdsSplitPoint,
                        semanticModel,
                        listPlans,
                        allGotoLoops,
                        allLoops,
                        switchesWithJoins);
                }

                break;
            case WhileStatementSyntax whileStatement:
                allLoops.Add(whileStatement);
                CollectStatementPlans(
                    whileStatement.Statement,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                break;
            case DoStatementSyntax doStatement:
                allLoops.Add(doStatement);
                CollectStatementPlans(
                    doStatement.Statement,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                break;
            case ForStatementSyntax forStatement:
                allLoops.Add(forStatement);
                CollectStatementPlans(
                    forStatement.Statement,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                break;
            case CommonForEachStatementSyntax forEachStatement:
                allLoops.Add(forEachStatement);
                CollectStatementPlans(
                    forEachStatement.Statement,
                    holdsSplitPoint,
                    semanticModel,
                    listPlans,
                    allGotoLoops,
                    allLoops,
                    switchesWithJoins);
                break;
            case SwitchStatementSyntax switchStatement:
                var sectionPlans = new JumpRegionPlan[switchStatement.Sections.Count];
                for (var s = 0; s < switchStatement.Sections.Count; s++)
                {
                    sectionPlans[s] = JumpRegionPlanner.Plan(switchStatement.Sections[s].Statements, holdsSplitPoint);
                }

                var crossSectionRejected =
                    JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, sectionPlans);
                if (HasCrossSectionSwitchJump(switchStatement, crossSectionRejected, semanticModel))
                {
                    switchesWithJoins.Add(switchStatement);
                }

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
                        CollectStatementPlans(
                            sectionStatement,
                            holdsSplitPoint,
                            semanticModel,
                            listPlans,
                            allGotoLoops,
                            allLoops,
                            switchesWithJoins);
                    }
                }

                break;
        }
    }

    private static bool HasCrossSectionSwitchJump(
        SwitchStatementSyntax switchStatement,
        ImmutableArray<CrossSectionRejectedGoto> crossSectionRejected,
        SemanticModel? semanticModel)
    {
        if (!crossSectionRejected.IsEmpty)
        {
            return true;
        }

        var sectionLookup = SwitchCaseLookup.CreateSectionLookup(switchStatement, semanticModel);
        var joinPlan = SwitchJoinPlanner.Plan(switchStatement, sectionLookup);
        if (!joinPlan.Joins.IsEmpty || !joinPlan.Rejected.IsEmpty)
        {
            return true;
        }

        foreach (var node in switchStatement.DescendantNodes(static n =>
                     n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is GotoStatementSyntax gotoStatement &&
                gotoStatement.IsKind(SyntaxKind.GotoCaseStatement, SyntaxKind.GotoDefaultStatement) &&
                ReconvergenceSyntax.EnclosingSwitchIs(gotoStatement, switchStatement) &&
                sectionLookup(gotoStatement) < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void PlanListCycles(
        SyntaxList<StatementSyntax> statements,
        JumpRegionPlan plan,
        StatementFlow flow,
        SemanticModel? semanticModel,
        ImmutableArray<(SyntaxList<StatementSyntax> Statements, JumpRegion Region)> allGotoLoops,
        HashSet<SwitchStatementSyntax> switchesWithJoins,
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

            if (!HasOnlyAllowedParents(backJumps, members, flow, owner, switchesWithJoins))
            {
                continue;
            }

            if (HasForwardGotoIntoLexicalRange(flow, statements, region, members))
            {
                continue;
            }

            var needsLatch = backJumps.Length >= 2 ||
                NeedsLatchForSingleBackJump(
                    backJumps[0], owner, statements, region, members, flow, semanticModel, allGotoLoops);
            var cycle = new Cycle(
                header,
                statements,
                region,
                members,
                backJumps,
                needsLatch);
            cycles[new CyclePlan.CycleKey(statements[region.First], region.Last)] = cycle;
            orderedCycles.Add(cycle);
        }
    }

    private static bool NeedsLatchForSingleBackJump(
        GotoStatementSyntax backJump,
        SyntaxNode owner,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        ImmutableHashSet<FlowNode> members,
        StatementFlow flow,
        SemanticModel? semanticModel,
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

            if (current is SwitchSectionSyntax section &&
                section.Parent is SwitchStatementSyntax switchStatement &&
                members.Contains(flow.ResolveNode(switchStatement)) &&
                HasIntraSectionSwitchJump(switchStatement, section, semanticModel))
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

    private static bool HasIntraSectionSwitchJump(
        SwitchStatementSyntax switchStatement,
        SwitchSectionSyntax section,
        SemanticModel? semanticModel)
    {
        var sectionIndex = switchStatement.Sections.IndexOf(section);
        if (sectionIndex < 0)
        {
            return false;
        }

        var hasSwitchGoto = section
            .DescendantNodes(static n =>
                n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .Any(switchStatement, static (node, @switch) =>
                node is GotoStatementSyntax gotoStatement &&
                gotoStatement.IsKind(SyntaxKind.GotoCaseStatement, SyntaxKind.GotoDefaultStatement) &&
                ReconvergenceSyntax.EnclosingSwitchIs(gotoStatement, @switch)
            );
        if (!hasSwitchGoto)
        {
            return false;
        }

        var sectionLookup = SwitchCaseLookup.CreateSectionLookup(switchStatement, semanticModel);
        return section
            .DescendantNodes(static n =>
                n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .Any((Switch: switchStatement, Index: sectionIndex, Lookup: sectionLookup), static (node, state) =>
                node is GotoStatementSyntax gotoStatement &&
                gotoStatement.IsKind(SyntaxKind.GotoCaseStatement, SyntaxKind.GotoDefaultStatement) &&
                ReconvergenceSyntax.EnclosingSwitchIs(gotoStatement, state.Switch) &&
                state.Lookup(gotoStatement) is var target &&
                (target == state.Index || target < 0)
            );
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
        SyntaxNode owner,
        HashSet<SwitchStatementSyntax> switchesWithJoins)
    {
        foreach (var backJump in backJumps)
        {
            if (!HasAllowedAncestors(backJump, owner, switchesWithJoins))
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
                (!HasAllowedAncestors(candidateGate, owner, switchesWithJoins) ||
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

    private static bool HasAllowedAncestors(
        SyntaxNode node,
        SyntaxNode owner,
        HashSet<SwitchStatementSyntax> switchesWithJoins)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current == owner)
            {
                return true;
            }

            if (current is SwitchStatementSyntax switchStatement)
            {
                if (switchesWithJoins.Contains(switchStatement))
                {
                    return false;
                }

                continue;
            }

            if (current is not (BlockSyntax or LabeledStatementSyntax or CheckedStatementSyntax
                or UnsafeStatementSyntax or IfStatementSyntax or ElseClauseSyntax
                or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax or SwitchSectionSyntax))
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

    private static void PlanLoopCycles(
        ImmutableArray<StatementSyntax> allLoops,
        StatementFlow flow,
        Func<StatementSyntax, bool> holdsSplitPoint,
        Dictionary<StatementSyntax, LoopCycle> loopCycles)
    {
        foreach (var loop in allLoops)
        {
            var body = GetLoopBody(loop);
            var splitScope = loop is ForStatementSyntax or CommonForEachStatementSyntax ? body : loop;
            if (!holdsSplitPoint(splitScope))
            {
                continue;
            }

            var backJumpsBuilder = ImmutableArray.CreateBuilder<ContinueStatementSyntax>();
            body.DescendantNodesAndSelf(static n =>
                    n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                .AsStackEnumerable()
                .OfType<ContinueStatementSyntax>()
                .Where(loop, static (cont, target) =>
                    ReconvergenceSyntax.FindJumpTarget(cont, loopOnly: true) == target)
                .AddTo(backJumpsBuilder);
            if (backJumpsBuilder.Count == 0)
            {
                continue;
            }

            var bodyEntry = StatementFlow.EntryOf(body);
            var headerNode = flow.ResolveNode(loop);
            var backTarget = loop is DoStatementSyntax
                ? new FlowNode(loop, FlowNodeKind.LoopTest)
                : headerNode;
            if (!flow.GetSuccessors(backTarget).Contains(bodyEntry))
            {
                continue;
            }

            var members = ComputeLoopMembers(flow, body, headerNode, backTarget);
            var headerNodes = new HashSet<FlowNode> { headerNode };
            if (HasExternalPredecessor(flow, headerNodes, members))
            {
                continue;
            }

            members = RestrictToReachableFromHeader(flow, headerNode, members);
            if (loop is DoStatementSyntax && !members.Contains(backTarget))
            {
                continue;
            }

            var liveBackJumps = ImmutableArray.CreateBuilder<ContinueStatementSyntax>(backJumpsBuilder.Count);
            backJumpsBuilder
                .AsStackEnumerable()
                .Where((flow, members), static (jump, state) =>
                    state.members.Contains(state.flow.ResolveNode(jump)))
                .AddTo(liveBackJumps);
            if (liveBackJumps.Count == 0)
            {
                continue;
            }

            var backJumps = liveBackJumps.ToImmutable();
            if (!HasOnlyRewritableLoopBackJumpParents(backJumps, loop))
            {
                continue;
            }

            if (HasForwardGotoOutsideMembersInBody(flow, body, members))
            {
                continue;
            }

            var gates = ImmutableHashSet.CreateBuilder<StatementSyntax>();
            var hasDisallowedGate = false;
            foreach (var member in members)
            {
                if (member.Kind != FlowNodeKind.Statement ||
                    member.Syntax is not StatementSyntax memberStatement ||
                    !ReconvergenceSyntax.IsInside(memberStatement, body) ||
                    !flow.TryGetListPosition(memberStatement, out var list, out var index) ||
                    index + 1 >= list.Count)
                {
                    continue;
                }

                var candidateGate = list[index + 1];
                if (CyclePlan.IsGateTransition(flow, members, memberStatement, candidateGate) &&
                    CyclePlan.HasBackJumpInside(backJumps, memberStatement))
                {
                    if (!HasAllowedLoopGateAncestors(candidateGate, loop))
                    {
                        hasDisallowedGate = true;
                        break;
                    }

                    gates.Add(candidateGate);
                }
            }

            if (hasDisallowedGate || gates.Count == 0)
            {
                continue;
            }

            loopCycles[loop] = new LoopCycle(loop, members, backJumps, gates.ToImmutable());
        }
    }

    private static ImmutableHashSet<FlowNode> ComputeLoopMembers(
        StatementFlow flow,
        StatementSyntax body,
        FlowNode headerNode,
        FlowNode backTarget)
    {
        var members = ImmutableHashSet.CreateBuilder<FlowNode>();
        var queue = new Queue<FlowNode>();
        members.Add(headerNode);
        if (backTarget != headerNode)
        {
            members.Add(backTarget);
        }

        foreach (var predecessor in flow.GetPredecessors(backTarget))
        {
            if (predecessor.Syntax is StatementSyntax predStatement &&
                ReconvergenceSyntax.IsInside(predStatement, body) &&
                members.Add(predecessor))
            {
                queue.Enqueue(predecessor);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var predecessor in flow.GetPredecessors(current))
            {
                if (predecessor.Syntax is StatementSyntax predStatement &&
                    ReconvergenceSyntax.IsInside(predStatement, body) &&
                    members.Add(predecessor))
                {
                    queue.Enqueue(predecessor);
                }
            }
        }

        return members.ToImmutable();
    }

    private static bool HasAllowedLoopGateAncestors(SyntaxNode node, StatementSyntax loop)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current == loop)
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

    private static bool HasOnlyRewritableLoopBackJumpParents(
        ImmutableArray<ContinueStatementSyntax> backJumps,
        StatementSyntax loop) =>
        backJumps
            .AsStackEnumerable()
            .All(loop, static (backJump, target) => HasRewritableLoopAncestors(backJump, target));

    private static bool HasRewritableLoopAncestors(SyntaxNode node, StatementSyntax loop)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current == loop)
            {
                return true;
            }

            if (current is not (BlockSyntax or LabeledStatementSyntax or CheckedStatementSyntax
                or UnsafeStatementSyntax or IfStatementSyntax or ElseClauseSyntax
                or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax or SwitchStatementSyntax or SwitchSectionSyntax))
            {
                return false;
            }
        }

        return false;
    }

    private static bool HasForwardGotoOutsideMembersInBody(
        StatementFlow flow,
        StatementSyntax body,
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
                JumpsToNonMemberLabelInsideBody(currentStatement, flow, body, members))
            {
                return true;
            }

            foreach (var successor in flow.GetSuccessors(current))
            {
                if (successor.Kind is not (FlowNodeKind.Statement or FlowNodeKind.LoopTest) ||
                    successor.Syntax is not StatementSyntax successorStatement ||
                    !ReconvergenceSyntax.IsInside(successorStatement, body))
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

    private static bool JumpsToNonMemberLabelInsideBody(
        StatementSyntax statement,
        StatementFlow flow,
        StatementSyntax body,
        ImmutableHashSet<FlowNode> members)
    {
        var inner = statement;
        while (inner is LabeledStatementSyntax labeled)
        {
            inner = labeled.Statement;
        }

        if (inner is GotoStatementSyntax directGoto)
        {
            return IsForwardGotoToNonMemberInBody(directGoto, flow, body, members);
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
                IsForwardGotoToNonMemberInBody(gotoStatement, flow, body, members))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForwardGotoToNonMemberInBody(
        GotoStatementSyntax gotoStatement,
        StatementFlow flow,
        StatementSyntax body,
        ImmutableHashSet<FlowNode> members)
    {
        if (!gotoStatement.IsKind(SyntaxKind.GotoStatement) ||
            gotoStatement.Expression is not IdentifierNameSyntax { Identifier.ValueText: var labelName } ||
            ReconvergenceSyntax.FindLabel(gotoStatement, labelName) is not { } targetLabel)
        {
            return false;
        }

        return !members.Contains(flow.ResolveNode(targetLabel)) &&
               ReconvergenceSyntax.IsInside(targetLabel, body);
    }
}
