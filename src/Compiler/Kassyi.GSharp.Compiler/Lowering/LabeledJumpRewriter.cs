namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers a labeled <c>break</c> or <c>continue</c> whose target loop or <c>switch</c> holds a split point (marked by
/// <see cref="LabeledJumpMarker"/>) to what the passes that flatten the target already handle.
/// </summary>
/// <remarks>
/// <para>
/// A jump that names the innermost loop (or <c>switch</c>, for <c>break</c>) loses its name and is the plain
/// <c>break;</c> / <c>continue;</c> that <see cref="LoopRewriter"/> and <see cref="SwitchJumpRewriter"/> rewrite. Any
/// other one becomes a <c>goto</c> that keeps the original span of the jump: a <c>break</c> to
/// <c>__gsharp_LabeledBreak_&lt;n&gt;</c>, placed right after the target statement, and a <c>continue</c> to
/// <c>__gsharp_LabeledContinue_&lt;n&gt;</c>, placed at the end of the body of the target loop, so that the increment
/// and the condition run after it. The target is flattened later and its own labels land next to these (the continue
/// label before the continue label of the flattened loop, the break label after its end label), so the state labels
/// and the jumps between states are decided as for any other <c>goto</c>.
/// </para>
/// <para>
/// Runs after <see cref="ReconvergenceInserter"/>, which decides its regions on the structure of the source (the break
/// label after the loop is in front of the point where the lanes meet again) and redirects a <c>continue</c> to a loop
/// with a body region itself, and before <see cref="BranchRewriter"/> and <see cref="LoopRewriter"/>, which leave a
/// named jump as it is. The new labels carry no original span: they are no label of the user's.
/// </para>
/// </remarks>
internal sealed class LabeledJumpRewriter : CSharpSyntaxRewriter, IBlockLoweringPass
{
    // The numbers of the targets that a goto breaks out of or continues, and the ones whose label has been placed
    private readonly HashSet<int> _breakLabels = [];
    private readonly HashSet<int> _continueLabels = [];
    private readonly HashSet<int> _placedBreakLabels = [];
    private readonly HashSet<int> _placedContinueLabels = [];

    /// <inheritdoc />
    public BlockSyntax Rewrite(BlockSyntax block)
    {
        _breakLabels.Clear();
        _continueLabels.Clear();
        _placedBreakLabels.Clear();
        _placedContinueLabels.Clear();
        var hasMarkedJump = false;
        foreach (var node in block.DescendantNodes())
        {
            if (node is not (BreakStatementSyntax or ContinueStatementSyntax) ||
                LabeledJumpMarker.GetNumber(node) < 0)
            {
                continue;
            }

            hasMarkedJump = true;
            if (NeedsGoto((StatementSyntax)node))
            {
                (node is BreakStatementSyntax ? _breakLabels : _continueLabels).Add(LabeledJumpMarker.GetNumber(node));
            }
        }

        if (!hasMarkedJump)
        {
            return block;
        }

        var rewritten = (BlockSyntax?)Visit(block) ?? block;
        if (!_placedBreakLabels.SetEquals(_breakLabels) || !_placedContinueLabels.SetEquals(_continueLabels))
        {
            throw new InvalidOperationException(
                "A labeled break or continue has no target statement left to place its label at: a lowering pass dropped it.");
        }

        return rewritten;
    }

    public override SyntaxNode? VisitBreakStatement(BreakStatementSyntax node) => RewriteJump(node);

    public override SyntaxNode? VisitContinueStatement(ContinueStatementSyntax node) => RewriteJump(node);

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax?)base.VisitBlock(node) ?? node;
        return visited.WithStatements(WithBreakLabels(node.Statements, visited.Statements));
    }

    public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
    {
        var visited = (SwitchSectionSyntax?)base.VisitSwitchSection(node) ?? node;
        return visited.WithStatements(WithBreakLabels(node.Statements, visited.Statements));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        var visited = (WhileStatementSyntax?)base.VisitWhileStatement(node) ?? node;
        return visited.WithStatement(WithContinueLabel(node, visited.Statement));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        var visited = (DoStatementSyntax?)base.VisitDoStatement(node) ?? node;
        return visited.WithStatement(WithContinueLabel(node, visited.Statement));
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var visited = (ForStatementSyntax?)base.VisitForStatement(node) ?? node;
        return visited.WithStatement(WithContinueLabel(node, visited.Statement));
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var visited = (ForEachStatementSyntax?)base.VisitForEachStatement(node) ?? node;
        return visited.WithStatement(WithContinueLabel(node, visited.Statement));
    }

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        var visited = (ForEachVariableStatementSyntax?)base.VisitForEachVariableStatement(node) ?? node;
        return visited.WithStatement(WithContinueLabel(node, visited.Statement));
    }

    // Whether the jump names a statement other than the one a jump without a name would leave or continue
    private static bool NeedsGoto(StatementSyntax jump)
    {
        var target = LabeledJumpMarker.FindTarget(jump)
                     ?? throw new InvalidOperationException(
                         "A labeled break or continue has no target statement left: a lowering pass dropped it.");
        return LabeledJumpSyntax.InnermostTarget(jump) != target;
    }

    // break; or continue; for a jump to the innermost target, otherwise goto <label>; with the span of the jump
    private static SyntaxNode RewriteJump(StatementSyntax jump)
    {
        var number = LabeledJumpMarker.GetNumber(jump);
        if (number < 0)
        {
            return jump;
        }

        if (!NeedsGoto(jump))
        {
            var plain = LabeledJumpSyntax.WithLabelName(jump, null);
            return plain.WithoutAnnotations(jump.GetAnnotations(LoweringAnnotations.LabeledJumpTargetAnnotationKind));
        }

        var prefix = jump is BreakStatementSyntax
            ? GSharpInternalNames.Lowering.LabeledBreakLabelPrefix
            : GSharpInternalNames.Lowering.LabeledContinueLabelPrefix;
        // goto <label>;
        return LoweringSyntax.Goto(prefix + number.ToString(CultureInfo.InvariantCulture))
            .WithTriviaFrom(jump)
            .WithOriginalSpanFrom(jump);
    }

    // <break label>: ; after each target statement of the list that a goto breaks out of
    private SyntaxList<StatementSyntax> WithBreakLabels(
        SyntaxList<StatementSyntax> original,
        SyntaxList<StatementSyntax> visited)
    {
        if (_breakLabels.Count == 0)
        {
            return visited;
        }

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(visited.Count + 1);
        for (var i = 0; i < visited.Count; i++)
        {
            statements.Add(visited[i]);
            if (LabeledJumpSyntax.IsBreakOrContinueTarget(original[i], includesSwitch: true) &&
                LabeledJumpMarker.GetNumber(original[i]) is var number and >= 0 &&
                _breakLabels.Contains(number))
            {
                _placedBreakLabels.Add(number);
                statements.Add(
                    LoweringSyntax.EmptyLabel(
                        GSharpInternalNames.Lowering.LabeledBreakLabelPrefix + number.ToString(CultureInfo.InvariantCulture)
                    )
                );
            }
        }

        return statements.Count == visited.Count ? visited : SyntaxFactory.List(statements);
    }

    // The body with <continue label>: ; at its end when a goto continues the loop
    private StatementSyntax WithContinueLabel(StatementSyntax loop, StatementSyntax body)
    {
        if (LabeledJumpMarker.GetNumber(loop) is not (var number and >= 0) || !_continueLabels.Contains(number))
        {
            return body;
        }

        _placedContinueLabels.Add(number);
        var label = LoweringSyntax.EmptyLabel(
            GSharpInternalNames.Lowering.LabeledContinueLabelPrefix + number.ToString(CultureInfo.InvariantCulture)
        );
        return body is BlockSyntax block ? block.AddStatements(label) : SyntaxFactory.Block(body, label);
    }
}
