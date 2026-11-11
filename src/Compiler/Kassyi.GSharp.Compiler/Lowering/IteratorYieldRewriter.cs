namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Replaces each <c>yield return</c> of an iterator body copied by <see cref="IteratorInliner"/> with a copy of the
/// consumer <c>foreach</c> body, and each <c>yield break</c> with a jump to the end label of the expansion.
/// </summary>
internal sealed class IteratorYieldRewriter(
    ForEachStatementSyntax annotatedForEach,
    TypeSyntax loopVariableType,
    bool needsElementCast,
    int iteratorRegion,
    string endLabel,
    SemanticModel semanticModel) : CSharpSyntaxRewriter
{
    private int _yieldCounter;
    private int _regionCounter;

    /// <summary>
    /// Rewrites <paramref name="body"/>; each consumer copy takes the next region after
    /// <paramref name="regionCounter"/>, which is left at the last one taken.
    /// </summary>
    public BlockSyntax Rewrite(BlockSyntax body, ref int regionCounter)
    {
        _regionCounter = regionCounter;
        var result = Visit(body) as BlockSyntax ?? body;
        regionCounter = _regionCounter;
        return result;
    }

    // { <ElementType> <loopVar> = <yieldedExpr>; { <consumerBody> } __gsharp_iter_next_<r>_<j>: ; }
    public override SyntaxNode? VisitYieldStatement(YieldStatementSyntax node)
    {
        if (node.IsKind(SyntaxKind.YieldBreakStatement))
        {
            // goto __gsharp_iter_end_<r>;
            return LoweringSyntax.Goto(endLabel).WithOriginalSpanFrom(node);
        }

        var yieldIndex = _yieldCounter++;
        var consumerRegion = ++_regionCounter;
        var nextLabel = $"{GSharpInternalNames.Iterator.NextLabelPrefix}{iteratorRegion}_{yieldIndex}";
        var yieldedExpression = GetYieldedExpression(node);
        var stampedConsumerEnvelope = CopyConsumer(nextLabel, consumerRegion);

        // <ElementType> <loopVar> = <yieldedExpr>;
        var stampedDeclStmt = (LocalDeclarationStatementSyntax)stampedConsumerEnvelope.Statements[0];
        var stampedDeclarator = stampedDeclStmt.Declaration.Variables[0]
            .WithInitializer(SyntaxFactory.EqualsValueClause(yieldedExpression));
        var loopVarDeclaration = stampedDeclStmt.WithDeclaration(
            stampedDeclStmt.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(stampedDeclarator)));
        var consumerBodyBlock = stampedConsumerEnvelope.Statements[1];

        var nextLabeledStmt = LoweringSyntax.EmptyLabel(nextLabel);

        return SyntaxFactory.Block(loopVarDeclaration, consumerBodyBlock, nextLabeledStmt)
            .WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.ScopedBlockAnnotationKind));
    }

    // <yielded> or (<ElementType>)(<yielded>) when the loop variable converts the element
    private ExpressionSyntax GetYieldedExpression(YieldStatementSyntax node)
    {
        var yieldedExpression = Visit(node.Expression) as ExpressionSyntax
                                ?? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
        if (needsElementCast)
        {
            yieldedExpression = SyntaxFactory.CastExpression(
                loopVariableType.WithoutTrivia(),
                SyntaxFactory.ParenthesizedExpression(yieldedExpression.WithoutTrivia()));
        }

        return yieldedExpression;
    }

    // { <ElementType> <loopVar>; { <consumerBody> } }, stamped with consumerRegion
    // Stamp consumerRegion onto the uninitialized loop variable declaration and consumer body together so that
    // references to the foreach loop variable and consumer-local declarations receive consumerRegion without
    // overwriting iteratorRegion on yieldedExpression.
    private BlockSyntax CopyConsumer(string nextLabel, int consumerRegion)
    {
        var uninitDeclarator = SyntaxFactory.VariableDeclarator(annotatedForEach.Identifier)
            .WithOriginalSpanFrom(annotatedForEach);
        var uninitVarDecl = SyntaxFactory.VariableDeclaration(
            loopVariableType,
            SyntaxFactory.SingletonSeparatedList(uninitDeclarator)
        );
        var uninitLocalDecl = SyntaxFactory.LocalDeclarationStatement(uninitVarDecl);

        var rewrittenConsumerBody = BranchTargetRewriter.Rewrite(annotatedForEach.Statement, endLabel, nextLabel);
        var rawConsumerBlock = (rewrittenConsumerBody is BlockSyntax block
                ? SyntaxFactory.Block(block.Statements)
                : SyntaxFactory.Block(rewrittenConsumerBody))
            .WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.ScopedBlockAnnotationKind));

        return LoweringAnnotations.StampRegion(
            SyntaxFactory.Block(uninitLocalDecl, rawConsumerBlock),
            consumerRegion,
            semanticModel);
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;
}
