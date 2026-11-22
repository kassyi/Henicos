namespace Kassyi.GSharp.Compiler.Lowering;

internal enum FlowNodeKind
{
    Statement,
    LoopTest,
    End
}

/// <summary>
/// One point of <see cref="StatementFlow"/>: the entry of a statement, the test of a <c>do</c> loop (after its body),
/// or the end of the method.
/// </summary>
internal readonly record struct FlowNode(SyntaxNode? Syntax, FlowNodeKind Kind);

/// <summary>
/// Statement-level control-flow graph of a lowered method body, folding C# constant boolean conditions.
/// </summary>
internal sealed class StatementFlow
{
    private static readonly FlowNode s_endNode = new(null, FlowNodeKind.End);

    private readonly Func<ExpressionSyntax, bool?> _constantCondition;
    private readonly Dictionary<FlowNode, ImmutableArray<FlowNode>> _successors;
    private readonly Dictionary<FlowNode, ImmutableArray<FlowNode>> _predecessors;
    private readonly Dictionary<StatementSyntax, (SyntaxList<StatementSyntax> List, int Index)> _listPositions;
    private readonly Dictionary<LabeledStatementSyntax, FlowNode> _labelTargets;
    private readonly HashSet<FlowNode> _knownNodes;
    private readonly Dictionary<SyntaxNode, FlowNode> _breakAfter;
    private readonly Dictionary<StatementSyntax, bool> _reachesEnd = new();

    private StatementFlow(
        Func<ExpressionSyntax, bool?> constantCondition,
        Dictionary<FlowNode, ImmutableArray<FlowNode>> successors,
        Dictionary<FlowNode, ImmutableArray<FlowNode>> predecessors,
        Dictionary<StatementSyntax, (SyntaxList<StatementSyntax> List, int Index)> listPositions,
        Dictionary<LabeledStatementSyntax, FlowNode> labelTargets,
        HashSet<FlowNode> knownNodes,
        Dictionary<SyntaxNode, FlowNode> breakAfter)
    {
        _constantCondition = constantCondition;
        _successors = successors;
        _predecessors = predecessors;
        _listPositions = listPositions;
        _labelTargets = labelTargets;
        _knownNodes = knownNodes;
        _breakAfter = breakAfter;
    }

    public static FlowNode EndNode => s_endNode;

    public static FlowNode EntryOf(StatementSyntax statement) => new(statement, FlowNodeKind.Statement);

    public static StatementFlow Build(BlockSyntax body, Func<ExpressionSyntax, bool?> constantCondition) =>
        new Builder(constantCondition).Build(body);

    public static bool ReachesEnd(StatementSyntax statement, Func<ExpressionSyntax, bool?> constantCondition) =>
        new Builder(constantCondition).ReachesEnd(statement);

    public ImmutableArray<FlowNode> GetSuccessors(FlowNode node) =>
        _successors.TryGetValue(node, out var list) ? list : ImmutableArray<FlowNode>.Empty;

    public ImmutableArray<FlowNode> GetPredecessors(FlowNode node) =>
        _predecessors.TryGetValue(node, out var list) ? list : ImmutableArray<FlowNode>.Empty;

    internal bool TryGetListPosition(StatementSyntax statement, out SyntaxList<StatementSyntax> list, out int index)
    {
        if (_listPositions.TryGetValue(statement, out var position))
        {
            list = position.List;
            index = position.Index;
            return true;
        }

        list = default;
        index = -1;
        return false;
    }

    internal bool TryGetBreakAfter(SyntaxNode statement, out FlowNode after) =>
        _breakAfter.TryGetValue(statement, out after);

    internal bool ReachesEnd(StatementSyntax statement)
    {
        if (_reachesEnd.TryGetValue(statement, out var reaches))
        {
            return reaches;
        }

        reaches = ReachesEnd(statement, _constantCondition);
        _reachesEnd[statement] = reaches;
        return reaches;
    }

    internal FlowNode ResolveNode(SyntaxNode node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is LabeledStatementSyntax labeled && _labelTargets.TryGetValue(labeled, out var labelNode))
            {
                return labelNode;
            }

            if (current is StatementSyntax statement)
            {
                var entry = EntryOf(statement);
                if (_knownNodes.Contains(entry))
                {
                    return entry;
                }
            }
        }

        return s_endNode;
    }

    private sealed class Builder(Func<ExpressionSyntax, bool?> constantCondition)
    {
        private readonly Dictionary<FlowNode, ImmutableArray<FlowNode>.Builder> _successors = new();
        private readonly Dictionary<FlowNode, ImmutableArray<FlowNode>.Builder> _predecessors = new();
        private readonly Dictionary<StatementSyntax, (SyntaxList<StatementSyntax> List, int Index)> _listPositions = new();
        private readonly Dictionary<LabeledStatementSyntax, FlowNode> _labelTargets = new();
        private readonly HashSet<FlowNode> _knownNodes = [s_endNode];
        private readonly Dictionary<SyntaxNode, FlowNode> _breakAfter = new();
        private readonly Dictionary<SyntaxNode, FlowNode> _continueTarget = new();
        private readonly Dictionary<SwitchStatementSyntax, ImmutableArray<FlowNode>> _switchSections = new();
        private readonly Dictionary<SwitchStatementSyntax, Func<GotoStatementSyntax, int>> _switchLookups = new();
        private readonly ImmutableArray<PendingJump>.Builder _pendingJumps = ImmutableArray.CreateBuilder<PendingJump>();

        public StatementFlow Build(BlockSyntax body)
        {
            BuildList(body.Statements, s_endNode);

            foreach (var pending in _pendingJumps)
            {
                ResolveJump(pending);
            }

            return new StatementFlow(
                constantCondition,
                Freeze(_successors),
                Freeze(_predecessors),
                _listPositions,
                _labelTargets,
                _knownNodes,
                _breakAfter);
        }

        public bool ReachesEnd(StatementSyntax statement)
        {
            var endSentinel = new FlowNode(statement, FlowNodeKind.End);
            _knownNodes.Add(endSentinel);
            var entry = BuildStatement(statement, endSentinel);

            foreach (var pending in _pendingJumps)
            {
                ResolveJump(pending);
            }

            return IsReachable(entry, endSentinel);
        }

        private bool IsReachable(FlowNode start, FlowNode target)
        {
            if (start == target)
            {
                return true;
            }

            var visited = new HashSet<FlowNode> { start };
            var queue = new Queue<FlowNode>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!_successors.TryGetValue(current, out var successors))
                {
                    continue;
                }

                foreach (var successor in successors)
                {
                    if (successor == target)
                    {
                        return true;
                    }

                    if (visited.Add(successor))
                    {
                        queue.Enqueue(successor);
                    }
                }
            }

            return false;
        }

        private FlowNode BuildList(SyntaxList<StatementSyntax> statements, FlowNode after)
        {
            var next = after;
            for (var i = statements.Count - 1; i >= 0; i--)
            {
                var statement = statements[i];
                _listPositions[statement] = (statements, i);
                next = BuildStatement(statement, next);
            }

            return next;
        }

        private FlowNode BuildStatement(StatementSyntax statement, FlowNode after)
        {
            var node = EntryOf(statement);
            _knownNodes.Add(node);

            var inner = statement;
            while (inner is LabeledStatementSyntax labeled)
            {
                _labelTargets[labeled] = node;
                inner = labeled.Statement;
            }

            switch (inner)
            {
                case BlockSyntax block:
                    AddEdge(node, BuildList(block.Statements, after));
                    break;
                case CheckedStatementSyntax checkedStatement:
                    AddEdge(node, BuildList(checkedStatement.Block.Statements, after));
                    break;
                case UnsafeStatementSyntax unsafeStatement:
                    AddEdge(node, BuildList(unsafeStatement.Block.Statements, after));
                    break;
                case IfStatementSyntax ifStatement:
                    var ifCondition = constantCondition(ifStatement.Condition);
                    var thenEntry = BuildStatement(ifStatement.Statement, after);
                    if (ifCondition != false)
                    {
                        AddEdge(node, thenEntry);
                    }

                    var elseEntry = ifStatement.Else is { } elseClause
                        ? BuildStatement(elseClause.Statement, after)
                        : after;
                    if (ifCondition != true)
                    {
                        AddEdge(node, elseEntry);
                    }

                    break;
                case WhileStatementSyntax whileStatement:
                    _breakAfter[whileStatement] = after;
                    _continueTarget[whileStatement] = node;
                    var whileBody = BuildStatement(whileStatement.Statement, node);
                    var whileCondition = constantCondition(whileStatement.Condition);
                    if (whileCondition != false)
                    {
                        AddEdge(node, whileBody);
                    }

                    if (whileCondition != true)
                    {
                        AddEdge(node, after);
                    }

                    break;
                case ForStatementSyntax forStatement:
                    _breakAfter[forStatement] = after;
                    _continueTarget[forStatement] = node;
                    var forBody = BuildStatement(forStatement.Statement, node);
                    var forCondition = forStatement.Condition is { } condition
                        ? constantCondition(condition)
                        : true;
                    if (forCondition != false)
                    {
                        AddEdge(node, forBody);
                    }

                    if (forCondition != true)
                    {
                        AddEdge(node, after);
                    }

                    break;
                case CommonForEachStatementSyntax forEachStatement:
                    _breakAfter[forEachStatement] = after;
                    _continueTarget[forEachStatement] = node;
                    var forEachBody = BuildStatement(forEachStatement.Statement, node);
                    AddEdge(node, forEachBody);
                    AddEdge(node, after);
                    break;
                case DoStatementSyntax doStatement:
                    var testNode = new FlowNode(doStatement, FlowNodeKind.LoopTest);
                    _knownNodes.Add(testNode);
                    _breakAfter[doStatement] = after;
                    _continueTarget[doStatement] = testNode;
                    var doBody = BuildStatement(doStatement.Statement, testNode);
                    AddEdge(node, doBody);
                    var doCondition = constantCondition(doStatement.Condition);
                    if (doCondition != false)
                    {
                        AddEdge(testNode, doBody);
                    }

                    if (doCondition != true)
                    {
                        AddEdge(testNode, after);
                    }

                    break;
                case SwitchStatementSyntax switchStatement:
                    _breakAfter[switchStatement] = after;
                    var sectionEntries = ImmutableArray.CreateBuilder<FlowNode>(switchStatement.Sections.Count);
                    var hasDefault = false;
                    foreach (var section in switchStatement.Sections)
                    {
                        if (section.Labels
                            .AsStackEnumerable()
                            .Any(IsUnconditionalDefaultLabel))
                        {
                            hasDefault = true;
                        }

                        var sectionEntry = BuildList(section.Statements, after);
                        sectionEntries.Add(sectionEntry);
                        AddEdge(node, sectionEntry);
                    }

                    _switchSections[switchStatement] = sectionEntries.ToImmutable();
                    if (!hasDefault)
                    {
                        AddEdge(node, after);
                    }

                    break;
                case GotoStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax
                    or ContinueStatementSyntax:
                    _pendingJumps.Add(new PendingJump(node, inner, Container: null));
                    break;
                default:
                    AddEdge(node, after);
                    if (inner is not LocalFunctionStatementSyntax)
                    {
                        foreach (var descendant in inner.DescendantNodes(static n =>
                                     n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                        {
                            if (descendant is GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax
                                or ReturnStatementSyntax)
                            {
                                _pendingJumps.Add(new PendingJump(node, (StatementSyntax)descendant, inner));
                            }
                        }
                    }

                    break;
            }

            return node;
        }

        private void ResolveJump(PendingJump pending)
        {
            switch (pending.Jump)
            {
                case ReturnStatementSyntax or ThrowStatementSyntax:
                    AddEdge(pending.FromNode, s_endNode);
                    break;
                case BreakStatementSyntax breakStatement:
                    if (ReconvergenceSyntax.FindJumpTarget(breakStatement, loopOnly: false) is { } breakTarget &&
                        (pending.Container == null || !ReconvergenceSyntax.IsInside(breakTarget, pending.Container)) &&
                        _breakAfter.TryGetValue(breakTarget, out var breakNode))
                    {
                        AddEdge(pending.FromNode, breakNode);
                    }

                    break;
                case ContinueStatementSyntax continueStatement:
                    if (ReconvergenceSyntax.FindJumpTarget(continueStatement, loopOnly: true) is { } continueTarget &&
                        (pending.Container == null || !ReconvergenceSyntax.IsInside(continueTarget, pending.Container)) &&
                        _continueTarget.TryGetValue(continueTarget, out var continueNode))
                    {
                        AddEdge(pending.FromNode, continueNode);
                    }

                    break;
                case GotoStatementSyntax gotoStatement when gotoStatement.IsKind(SyntaxKind.GotoStatement):
                    if (gotoStatement.Expression is IdentifierNameSyntax { Identifier.ValueText: var labelName } &&
                        ReconvergenceSyntax.FindLabel(gotoStatement, labelName) is { } targetLabel)
                    {
                        if (_labelTargets.TryGetValue(targetLabel, out var labelNode))
                        {
                            AddEdge(pending.FromNode, labelNode);
                        }
                        else if (pending.Container == null || !ReconvergenceSyntax.IsInside(targetLabel, pending.Container))
                        {
                            AddEdge(pending.FromNode, s_endNode);
                        }
                    }
                    else
                    {
                        AddEdge(pending.FromNode, s_endNode);
                    }

                    break;
                case GotoStatementSyntax gotoCase when gotoCase.IsKind(SyntaxKind.GotoCaseStatement) ||
                                                       gotoCase.IsKind(SyntaxKind.GotoDefaultStatement):
                    if (FindEnclosingSwitch(gotoCase) is { } enclosingSwitch &&
                        (pending.Container == null || !ReconvergenceSyntax.IsInside(enclosingSwitch, pending.Container)) &&
                        _switchSections.TryGetValue(enclosingSwitch, out var entries))
                    {
                        if (!_switchLookups.TryGetValue(enclosingSwitch, out var lookup))
                        {
                            lookup = SwitchCaseLookup.CreateSectionLookup(enclosingSwitch, semanticModel: null);
                            _switchLookups[enclosingSwitch] = lookup;
                        }

                        var targetSection = lookup(gotoCase);
                        if (targetSection >= 0 && targetSection < entries.Length)
                        {
                            AddEdge(pending.FromNode, entries[targetSection]);
                        }
                        else
                        {
                            foreach (var entry in entries)
                            {
                                AddEdge(pending.FromNode, entry);
                            }
                        }
                    }

                    break;
            }
        }

        private static bool IsUnconditionalDefaultLabel(SwitchLabelSyntax label) =>
            label is DefaultSwitchLabelSyntax or
                CasePatternSwitchLabelSyntax
                {
                    Pattern: DiscardPatternSyntax or VarPatternSyntax,
                    WhenClause: null
                };

        private static SwitchStatementSyntax? FindEnclosingSwitch(SyntaxNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current is SwitchStatementSyntax switchStatement)
                {
                    return switchStatement;
                }

                if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax)
                {
                    return null;
                }
            }

            return null;
        }

        private void AddEdge(FlowNode from, FlowNode to)
        {
            if (!_successors.TryGetValue(from, out var succ))
            {
                succ = ImmutableArray.CreateBuilder<FlowNode>();
                _successors[from] = succ;
            }

            if (!succ.Contains(to))
            {
                succ.Add(to);
            }

            if (!_predecessors.TryGetValue(to, out var pred))
            {
                pred = ImmutableArray.CreateBuilder<FlowNode>();
                _predecessors[to] = pred;
            }

            if (!pred.Contains(from))
            {
                pred.Add(from);
            }
        }

        private static Dictionary<FlowNode, ImmutableArray<FlowNode>> Freeze(
            Dictionary<FlowNode, ImmutableArray<FlowNode>.Builder> builders)
        {
            var result = new Dictionary<FlowNode, ImmutableArray<FlowNode>>(builders.Count);
            foreach (var entry in builders)
            {
                result[entry.Key] = entry.Value.ToImmutable();
            }

            return result;
        }

        private readonly record struct PendingJump(FlowNode FromNode, StatementSyntax Jump, StatementSyntax? Container);
    }
}
