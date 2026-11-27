using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

// A loop that may need an iteration key of Warp.ActiveMask() on CUDA, and where its counter is reset and stepped
internal sealed class LoopCandidate
{
    private LoopCandidate(
        StatementSyntax headerStatement,
        StatementSyntax? structuredLoop,
        StatementSyntax? structuredBody,
        CyclePlan? cyclePlan,
        LoopCycle? loopCycle,
        SyntaxList<StatementSyntax> gotoStatements,
        SyntaxNode? gotoOwner,
        int gotoFirst,
        int gotoLast,
        Cycle? gotoCycle,
        LabeledStatementSyntax? incrementLabel,
        ImmutableArray<StatementSyntax> preResetStatements,
        ImmutableArray<LabeledStatementSyntax> postLabelResetLabels,
        SwitchStatementSyntax? switchOwner,
        SwitchSectionSyntax? switchSection,
        ImmutableArray<SwitchSectionSyntax> switchSccSections,
        int switchHeaderIndex,
        bool incrementSwitchSection,
        ImmutableArray<LabeledStatementSyntax> switchIncrementLabels,
        ImmutableHashSet<FlowNode>? switchSectionMembers)
    {
        HeaderStatement = headerStatement;
        StructuredLoop = structuredLoop;
        StructuredBody = structuredBody;
        CyclePlan = cyclePlan;
        LoopCycle = loopCycle;
        GotoStatements = gotoStatements;
        GotoOwner = gotoOwner;
        GotoFirst = gotoFirst;
        GotoLast = gotoLast;
        GotoCycle = gotoCycle;
        IncrementLabel = incrementLabel;
        PreResetStatements = preResetStatements;
        PostLabelResetLabels = postLabelResetLabels;
        SwitchOwner = switchOwner;
        SwitchSection = switchSection;
        SwitchSccSections = switchSccSections;
        SwitchHeaderIndex = switchHeaderIndex;
        IncrementSwitchSection = incrementSwitchSection;
        SwitchIncrementLabels = switchIncrementLabels;
        SwitchSectionMembers = switchSectionMembers;
    }

    public StatementSyntax HeaderStatement { get; }
    public StatementSyntax? StructuredLoop { get; }
    public StatementSyntax? StructuredBody { get; }
    public CyclePlan? CyclePlan { get; }
    public LoopCycle? LoopCycle { get; }
    public SyntaxList<StatementSyntax> GotoStatements { get; }
    public SyntaxNode? GotoOwner { get; }
    public int GotoFirst { get; }
    public int GotoLast { get; }
    public Cycle? GotoCycle { get; }
    public LabeledStatementSyntax? IncrementLabel { get; }
    public ImmutableArray<StatementSyntax> PreResetStatements { get; }
    public ImmutableArray<LabeledStatementSyntax> PostLabelResetLabels { get; }
    public SwitchStatementSyntax? SwitchOwner { get; }
    public SwitchSectionSyntax? SwitchSection { get; }
    public ImmutableArray<SwitchSectionSyntax> SwitchSccSections { get; }
    public int SwitchHeaderIndex { get; }
    public bool IncrementSwitchSection { get; }
    public ImmutableArray<LabeledStatementSyntax> SwitchIncrementLabels { get; }
    public ImmutableHashSet<FlowNode>? SwitchSectionMembers { get; }

    public static LoopCandidate ForStructuredLoop(
        StatementSyntax loop,
        StatementSyntax body,
        CyclePlan cyclePlan,
        LoopCycle? loopCycle) =>
        new(
            ActiveMaskIterationPlanner.OuterLabeledStatement(loop),
            loop,
            body,
            cyclePlan,
            loopCycle,
            default,
            null,
            -1,
            -1,
            null,
            null,
            ImmutableArray<StatementSyntax>.Empty,
            ImmutableArray<LabeledStatementSyntax>.Empty,
            null,
            null,
            ImmutableArray<SwitchSectionSyntax>.Empty,
            0,
            false,
            ImmutableArray<LabeledStatementSyntax>.Empty,
            null);

    public static LoopCandidate ForGotoLoop(
        StatementSyntax header,
        SyntaxList<StatementSyntax> statements,
        SyntaxNode owner,
        int first,
        int last,
        CyclePlan cyclePlan,
        Cycle? cycle,
        LabeledStatementSyntax? incrementLabel,
        ImmutableArray<StatementSyntax> preResetStatements,
        ImmutableArray<LabeledStatementSyntax> postLabelResetLabels) =>
        new(
            header,
            null,
            null,
            cyclePlan,
            null,
            statements,
            owner,
            first,
            last,
            cycle,
            incrementLabel,
            preResetStatements,
            postLabelResetLabels,
            null,
            null,
            ImmutableArray<SwitchSectionSyntax>.Empty,
            0,
            false,
            ImmutableArray<LabeledStatementSyntax>.Empty,
            null);

    public static LoopCandidate ForSwitchSection(
        SwitchStatementSyntax switchOwner,
        SwitchSectionSyntax switchSection,
        ImmutableArray<SwitchSectionSyntax> sccSections,
        StatementSyntax headerStatement,
        int headerIndex,
        bool incrementSwitchSection,
        ImmutableArray<LabeledStatementSyntax> switchIncrementLabels,
        ImmutableHashSet<FlowNode> members) =>
        new(
            headerStatement,
            null,
            null,
            null,
            null,
            default,
            null,
            -1,
            -1,
            null,
            null,
            ImmutableArray<StatementSyntax>.Empty,
            ImmutableArray<LabeledStatementSyntax>.Empty,
            switchOwner,
            switchSection,
            sccSections,
            headerIndex,
            incrementSwitchSection,
            switchIncrementLabels,
            members);

    public IEnumerable<SyntaxNode> GetExtentRoots()
    {
        if (SwitchSection != null && SwitchOwner != null)
        {
            if (SwitchSccSections.Length > 1)
            {
                yield return SwitchOwner.Expression;
            }

            foreach (var sccSection in SwitchSccSections)
            {
                foreach (var label in sccSection.Labels)
                {
                    if (label is CasePatternSwitchLabelSyntax { WhenClause: { } whenClause })
                    {
                        yield return whenClause.Condition;
                    }
                }

                foreach (var stmt in sccSection.Statements)
                {
                    yield return stmt;
                }
            }

            yield break;
        }

        if (StructuredLoop != null && StructuredBody != null)
        {
            yield return StructuredBody;
            switch (StructuredLoop)
            {
                case ForStatementSyntax forStatement:
                    if (forStatement.Condition != null)
                    {
                        yield return forStatement.Condition;
                    }

                    foreach (var incrementor in forStatement.Incrementors)
                    {
                        yield return incrementor;
                    }

                    break;
                case WhileStatementSyntax whileStatement:
                    yield return whileStatement.Condition;
                    break;
                case DoStatementSyntax doStatement:
                    yield return doStatement.Condition;
                    break;
            }

            yield break;
        }

        for (var i = GotoFirst; i <= GotoLast; i++)
        {
            yield return GotoStatements[i];
        }
    }

    public bool ContainsMemberStatement(StatementSyntax statement)
    {
        var outer = ActiveMaskIterationPlanner.OuterLabeledStatement(statement);
        if (SwitchSection != null)
        {
            return SwitchSectionMembers != null &&
                   SwitchSectionMembers.Contains(StatementFlow.EntryOf(outer));
        }

        if (StructuredLoop != null)
        {
            if (LoopCycle != null)
            {
                return LoopCycle.Members.Contains(StatementFlow.EntryOf(outer));
            }

            return IsInStructuredExtent(outer);
        }

        if (GotoCycle != null)
        {
            return GotoCycle.Members.Contains(StatementFlow.EntryOf(outer));
        }

        return IsInGotoExtent(outer);
    }

    public bool Holds(InvocationExpressionSyntax call, StatementSyntax containingStatement)
    {
        var outerStatement = ActiveMaskIterationPlanner.OuterLabeledStatement(containingStatement);
        if (SwitchSection != null)
        {
            return IsInSwitchSectionExtent(outerStatement);
        }

        if (StructuredLoop != null)
        {
            if (StructuredLoop == containingStatement)
            {
                return IsInStructuredHeaderExtent(call, StructuredLoop);
            }

            if (!IsInStructuredExtent(outerStatement))
            {
                return false;
            }

            if (LoopCycle == null)
            {
                return true;
            }

            if (LoopCycle.Members.Contains(StatementFlow.EntryOf(outerStatement)))
            {
                return true;
            }

            return !IsPastStructuredGate(outerStatement);
        }

        if (!IsInGotoExtent(outerStatement))
        {
            return false;
        }

        if (GotoCycle == null)
        {
            return true;
        }

        if (GotoCycle.Members.Contains(StatementFlow.EntryOf(outerStatement)))
        {
            return true;
        }

        return !IsPastGotoGate(outerStatement);
    }

    private bool IsInSwitchSectionExtent(SyntaxNode node)
    {
        if (SwitchSection == null)
        {
            return false;
        }

        for (var current = node; current != null; current = current.Parent)
        {
            if (ActiveMaskIterationPlanner.IsNestedCallable(current))
            {
                return false;
            }

            if (current is StatementSyntax statement && statement.Parent == SwitchSection)
            {
                var index = SwitchSection.Statements.IndexOf(statement);
                return index >= SwitchHeaderIndex;
            }
        }

        return false;
    }

    private static bool IsInStructuredHeaderExtent(InvocationExpressionSyntax call, StatementSyntax loop) =>
        loop switch
        {
            ForStatementSyntax forStatement =>
                (forStatement.Condition != null && forStatement.Condition.Span.Contains(call.Span)) ||
                forStatement.Incrementors
                    .AsStackEnumerable()
                    .Any(call.Span, static (inc, span) => inc.Span.Contains(span)),
            WhileStatementSyntax whileStatement => whileStatement.Condition.Span.Contains(call.Span),
            DoStatementSyntax doStatement => doStatement.Condition.Span.Contains(call.Span),
            _ => false
        };

    private bool IsInStructuredExtent(SyntaxNode node)
    {
        if (StructuredBody == null)
        {
            return false;
        }

        for (var current = node; current != null; current = current.Parent)
        {
            if (ActiveMaskIterationPlanner.IsNestedCallable(current))
            {
                return false;
            }

            if (current == StructuredBody)
            {
                return true;
            }

            if (current == StructuredLoop)
            {
                return false;
            }
        }

        return false;
    }

    private bool IsInGotoExtent(SyntaxNode node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (ActiveMaskIterationPlanner.IsNestedCallable(current))
            {
                return false;
            }

            if (current is StatementSyntax statement &&
                statement.Parent == GotoOwner)
            {
                var index = GotoStatements.IndexOf(statement);
                return index >= GotoFirst && index <= GotoLast;
            }
        }

        return false;
    }

    private bool IsPastStructuredGate(StatementSyntax outerStatement)
    {
        if (CyclePlan == null || LoopCycle == null || StructuredBody == null)
        {
            return false;
        }

        SyntaxNode? current = outerStatement;
        while (current != null)
        {
            if (current is StatementSyntax stmt)
            {
                var stmtOuter = ActiveMaskIterationPlanner.OuterLabeledStatement(stmt);
                if (CyclePlan.IsGateStatement(LoopCycle, stmtOuter))
                {
                    return true;
                }

                if (IterationCounterResets.TryGetOwningList(stmtOuter, out var list))
                {
                    var index = list.IndexOf(stmtOuter);
                    for (var k = 0; k <= index; k++)
                    {
                        if (CyclePlan.IsGateStatement(LoopCycle, list[k]))
                        {
                            return true;
                        }
                    }
                }

                if (stmtOuter.Parent == StructuredBody || stmtOuter == ActiveMaskIterationPlanner.OuterLabeledStatement(StructuredBody))
                {
                    return false;
                }
            }

            if (current == StructuredBody || current == StructuredLoop)
            {
                return false;
            }

            current = current.Parent;
        }

        return false;
    }

    private bool IsPastGotoGate(StatementSyntax outerStatement)
    {
        if (CyclePlan == null || GotoCycle == null)
        {
            return false;
        }

        SyntaxNode? current = outerStatement;
        while (current != null)
        {
            if (current is StatementSyntax stmt)
            {
                var stmtOuter = ActiveMaskIterationPlanner.OuterLabeledStatement(stmt);
                if (CyclePlan.IsGateStatement(GotoCycle, stmtOuter))
                {
                    return true;
                }

                if (IterationCounterResets.TryGetOwningList(stmtOuter, out var list))
                {
                    var index = list.IndexOf(stmtOuter);
                    for (var k = 0; k <= index; k++)
                    {
                        if (CyclePlan.IsGateStatement(GotoCycle, list[k]))
                        {
                            return true;
                        }
                    }
                }

                if (stmtOuter.Parent == GotoOwner)
                {
                    return false;
                }
            }

            if (current == GotoOwner)
            {
                return false;
            }

            current = current.Parent;
        }

        return false;
    }
}
