namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Finds where a goto loop of <see cref="JumpRegionPlanner"/> closes when its last statement is a block: right after the
/// statement inside the block that holds the last goto back to the label of the loop, looking through nested blocks.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] On the GPU the lanes that leave a goto loop run together again right after the statement that holds its
/// last backward goto. A block (with labels, <c>checked</c>, <c>unchecked</c> or <c>unsafe</c>) adds no control flow,
/// so when the last statement of the loop is a block, the loop ends inside it, and the statements after that goto in
/// the block already run with every lane. <c>if</c> statements and loops are not looked through: where the GPU runs the
/// lanes together after a goto inside them does not follow from the source alone (#2234 (A goto loop whose last
/// backward goto is inside a block, an if or a loop does not reconverge where CUDA does on the CPU paths)).
/// </para>
/// <para>
/// The loop closes inside the block only when nothing else is open across the new place: no other goto loop ends at
/// the same statement inside the loop, no region of the outer list or of a nested list is open across it, no goto
/// before it jumps to a label after it inside the last statement, and no goto after it jumps back into the loop.
/// Otherwise the loop closes after its last statement, as before.
/// </para>
/// </remarks>
internal static class GotoLoopTailPlanner
{
    /// <summary>
    /// The indices, outermost first, of the statements inside the last statement of <paramref name="loop"/> after which
    /// the loop closes: each index is in the statements of the block that the previous one names (the first in the
    /// block of <c>statements[loop.Last]</c>). Empty when the loop closes after <c>statements[loop.Last]</c>.
    /// </summary>
    /// <param name="statements">The list that holds the loop.</param>
    /// <param name="plan">The plan of <paramref name="statements"/> that holds <paramref name="loop"/>.</param>
    /// <param name="loop">The goto loop.</param>
    /// <param name="holdsSplitPoint">Whether a statement holds a split point, as passed to the planner.</param>
    public static ImmutableArray<int> FindInnerClose(
        SyntaxList<StatementSyntax> statements,
        JumpRegionPlan plan,
        JumpRegion loop,
        Func<StatementSyntax, bool> holdsSplitPoint)
    {
        if (loop.Kind != JumpRegionKind.GotoLoop || !IsInnermostAtItsEnd(plan, loop))
        {
            return ImmutableArray<int>.Empty;
        }

        var canonical = JumpRegionPlanner.BuildCanonicalLabelIndices(statements);
        var target = new LoopLabels(statements, canonical, loop.LabelIndex);
        var path = ImmutableArray.CreateBuilder<int>();
        StatementSyntax? closeAfter = null;
        var block = GetBlock(statements[loop.Last]);
        while (block != null)
        {
            var body = block.Statements;
            var q = FindLastGotoTo(body, target);
            if (q < 0)
            {
                return ImmutableArray<int>.Empty;
            }

            var bodyPlan = JumpRegionPlanner.Plan(body, holdsSplitPoint);
            var nested = GetBlock(body[q]);
            if (!bodyPlan.Rejected.IsEmpty || IsOpenAcross(bodyPlan, q, closesHere: nested == null))
            {
                return ImmutableArray<int>.Empty;
            }

            path.Add(q);
            closeAfter = body[q];
            block = nested;
        }

        if (closeAfter == null)
        {
            return ImmutableArray<int>.Empty;
        }

        var bounds = new CloseBounds(
            closeAfter.Span.End,
            statements[loop.First].SpanStart,
            statements[loop.Last]);
        for (var k = loop.First; k <= loop.Last; k++)
        {
            if (JumpsAcrossClose(statements[k], bounds))
            {
                return ImmutableArray<int>.Empty;
            }
        }

        return path.ToImmutable();
    }

    /// <summary>
    /// The block that <paramref name="statement"/> is, past its labels: a block, or the block of a <c>checked</c>,
    /// <c>unchecked</c> or <c>unsafe</c> statement; <see langword="null"/> for any other statement.
    /// </summary>
    public static BlockSyntax? GetBlock(StatementSyntax statement)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        return statement switch
        {
            BlockSyntax block => block,
            CheckedStatementSyntax checkedStatement => checkedStatement.Block,
            UnsafeStatementSyntax unsafeStatement => unsafeStatement.Block,
            _ => null
        };
    }

    // No other goto loop ends at the same statement inside the loop, and no region that opens after the loop is still
    // open at the end of its last statement (it would be above the loop where the loop closes)
    private static bool IsInnermostAtItsEnd(JumpRegionPlan plan, JumpRegion loop)
    {
        var opensAfterLoop = false;
        foreach (var region in plan.Regions)
        {
            if (ReferenceEquals(region, loop))
            {
                opensAfterLoop = true;
                continue;
            }

            if (region.Kind == JumpRegionKind.GotoLoop && region.Last == loop.Last && region.First >= loop.First)
            {
                return false;
            }

            if (opensAfterLoop && region.First <= loop.Last && IsOpenAfter(region, loop.Last, closesAfter: false))
            {
                return false;
            }
        }

        return true;
    }

    // Whether a region of the list that holds the close is open across it. In the list where the loop closes (after
    // statements[q]) a goto loop that ends at q has closed already; in a list the close only passes through (inside
    // statements[q]) it has not
    private static bool IsOpenAcross(JumpRegionPlan plan, int q, bool closesHere) =>
        plan.Regions
            .AsStackEnumerable()
            .Any((q, closesHere), static (region, close) =>
                region.First <= close.q && IsOpenAfter(region, close.q, close.closesHere)
            );

    // Whether region is still open after statements[index] (closesAfter) or while statements[index] is rewritten
    private static bool IsOpenAfter(JumpRegion region, int index, bool closesAfter) =>
        region.Kind == JumpRegionKind.GotoLoop
            ? closesAfter ? region.Last > index : region.Last >= index
            : region.LabelIndex > index;

    // The index of the last statement of body that holds a goto to a label of the loop, or -1
    private static int FindLastGotoTo(SyntaxList<StatementSyntax> body, LoopLabels target)
    {
        for (var q = body.Count - 1; q >= 0; q--)
        {
            if (Gotos(body[q])
                .AsStackEnumerable()
                .Any(target, static (node, labels) => IsGotoTo(node, labels)))
            {
                return q;
            }
        }

        return -1;
    }

    private static bool IsGotoTo(SyntaxNode node, LoopLabels labels) =>
        FindTargetLabel(node) is { } label &&
        JumpRegionPlanner.FindStatementIndexForLabel(labels.Statements, label) is var raw and >= 0 &&
        labels.Canonical[raw] == labels.LabelIndex;

    // A goto before the close to a label after it inside the last statement (it would run past the close inside the
    // loop), or a goto after the close to a label inside the loop before it (it would come back into the closed loop)
    private static bool JumpsAcrossClose(StatementSyntax statement, CloseBounds bounds) =>
        Gotos(statement)
            .AsStackEnumerable()
            .Any(bounds, static (node, close) =>
                FindTargetLabel(node) is { } label &&
                (node.SpanStart < close.Position
                    ? label.SpanStart >= close.Position && ReconvergenceSyntax.IsInside(label, close.Tail)
                    : label.SpanStart < close.Position && label.SpanStart >= close.LoopStart)
            );

    // The nodes of statement outside lambdas and local functions, whose gotos jump within the method
    private static IEnumerable<SyntaxNode> Gotos(StatementSyntax statement) =>
        statement.DescendantNodesAndSelf(static n =>
            n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

    private static LabeledStatementSyntax? FindTargetLabel(SyntaxNode node) =>
        node is GotoStatementSyntax gotoStatement &&
        gotoStatement.IsKind(SyntaxKind.GotoStatement) &&
        gotoStatement.Expression is IdentifierNameSyntax { Identifier.ValueText: var name }
            ? ReconvergenceSyntax.FindLabel(gotoStatement, name)
            : null;

    // The labels of the loop: those of the statements of the outer list whose canonical index is the loop's label index
    private readonly record struct LoopLabels(SyntaxList<StatementSyntax> Statements, int[] Canonical, int LabelIndex);

    // Position: where the loop closes; LoopStart: where the loop starts, its labels included; Tail: its last statement
    private readonly record struct CloseBounds(int Position, int LoopStart, StatementSyntax Tail);
}
