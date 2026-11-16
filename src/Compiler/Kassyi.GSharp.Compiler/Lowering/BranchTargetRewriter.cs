namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Rewrites top-level <c>break</c> and <c>continue</c> statements of a consumer <c>foreach</c> body into <c>goto</c>
/// statements targeting the enclosing inlined iterator expansion's end and next labels.
/// </summary>
internal sealed class BranchTargetRewriter(string endLabel, string nextLabel, string? loopLabel) : CSharpSyntaxRewriter
{
    private int _breakDepth;
    private int _continueDepth;

    public static StatementSyntax Rewrite(StatementSyntax body, string endLabel, string nextLabel,
        string? loopLabel = null)
    {
        var resolvedLoopLabel = loopLabel ??
            (body.Parent is CommonForEachStatementSyntax { Parent: LabeledStatementSyntax labeled }
                ? labeled.Identifier.ValueText
                : null);
        var rewriter = new BranchTargetRewriter(endLabel, nextLabel, resolvedLoopLabel);
        return rewriter.Visit(body) as StatementSyntax ?? body;
    }

    public override SyntaxNode? VisitBreakStatement(BreakStatementSyntax node)
    {
        if (LabeledJumpSyntax.GetLabelName(node) is { } labelName)
        {
            return NamesThisLoop(labelName)
                ? LoweringSyntax.Goto(endLabel).WithOriginalSpanFrom(node)
                : node;
        }

        if (_breakDepth > 0)
        {
            return node;
        }

        // goto <endLabel>;
        return LoweringSyntax.Goto(endLabel).WithOriginalSpanFrom(node);
    }

    public override SyntaxNode? VisitContinueStatement(ContinueStatementSyntax node)
    {
        if (LabeledJumpSyntax.GetLabelName(node) is { } labelName)
        {
            return NamesThisLoop(labelName)
                ? LoweringSyntax.Goto(nextLabel).WithOriginalSpanFrom(node)
                : node;
        }

        if (_continueDepth > 0)
        {
            return node;
        }

        // goto <nextLabel>;
        return LoweringSyntax.Goto(nextLabel).WithOriginalSpanFrom(node);
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) =>
        VisitLoop(node, base.VisitWhileStatement);

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
        VisitLoop(node, base.VisitDoStatement);

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
        VisitLoop(node, base.VisitForStatement);

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
        VisitLoop(node, base.VisitForEachStatement);

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
        VisitLoop(node, base.VisitForEachVariableStatement);

    public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        _breakDepth++;
        try
        {
            return base.VisitSwitchStatement(node);
        }
        finally
        {
            _breakDepth--;
        }
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

    private SyntaxNode? VisitLoop<TNode>(TNode node, Func<TNode, SyntaxNode?> visitBase) where TNode : SyntaxNode
    {
        _breakDepth++;
        _continueDepth++;
        try
        {
            return visitBase(node);
        }
        finally
        {
            _breakDepth--;
            _continueDepth--;
        }
    }

    // Whether a labeled jump names the consumer foreach whose body this rewriter walks
    private bool NamesThisLoop(IdentifierNameSyntax labelName) =>
        loopLabel != null && string.Equals(labelName.Identifier.ValueText, loopLabel, StringComparison.Ordinal);
}
