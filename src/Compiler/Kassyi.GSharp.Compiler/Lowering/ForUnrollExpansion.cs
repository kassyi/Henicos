namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands a <c>Gpu.ForUnroll</c> whose body holds a phase barrier into a standard <c>for</c> loop
/// (<see cref="PreLoweringExpander"/>).
/// </summary>
internal static class ForUnrollExpansion
{
    /// <summary>
    /// The loop that <paramref name="node"/> expands into, or <see langword="null"/> when the call stays as written:
    /// its body holds no barrier, or its start and count are not constants that fit an <see langword="int"/>.
    /// </summary>
    public static ForStatementSyntax? TryExpand(
        ExpressionStatementSyntax node,
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        SemanticModel? semanticModel,
        ref int expansionCounter)
    {
        var args = invocation.ArgumentList.Arguments;
        if (!MacroExpansionSyntax.TryGetLoopLambda(call, args, out var loop) ||
            call.GetArgument(args, call.ValueOrdinal)?.Expression is not { } startArgument ||
            call.GetArgument(args, call.ValueOrdinal + 1)?.Expression is not { } countArgument)
        {
            return null;
        }

        var startExpr = startArgument.WithoutTrivia();
        var countExpr = countArgument.WithoutTrivia();
        if (!TryBuildBoundExpression(startExpr, countExpr, semanticModel, out var boundExpr))
        {
            return null;
        }

        var id = expansionCounter++;
        var indexName = $"{GSharpInternalNames.Lowering.UnrollIndexPrefix}{id}";
        var annotations = MacroExpansionSyntax.GetOriginalSpans(node);
        var bodyBlock = MacroExpansionSyntax.SubstituteBody(loop, semanticModel, indexName, annotations);
        return CreateLoop(indexName, startExpr, boundExpr, bodyBlock, annotations);
    }

    // for (int __gsharp_unroll_<id> = <startExpr>; __gsharp_unroll_<id> < <boundExpr>; __gsharp_unroll_<id>++)
    //     <bodyBlock>
    private static ForStatementSyntax CreateLoop(
        string indexName,
        ExpressionSyntax startExpr,
        ExpressionSyntax boundExpr,
        BlockSyntax bodyBlock,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        var indexDeclaration = MacroExpansionSyntax.DeclareVariable(
            SyntaxKind.IntKeyword,
            indexName,
            startExpr,
            annotations
        );

        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(indexName),
            boundExpr
        ).WithAdditionalAnnotations(annotations);

        var incrementor = SyntaxFactory.PostfixUnaryExpression(
            SyntaxKind.PostIncrementExpression,
            SyntaxFactory.IdentifierName(indexName)
        ).WithAdditionalAnnotations(annotations);

        return SyntaxFactory.ForStatement(
            indexDeclaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(incrementor),
            bodyBlock
        ).WithAdditionalAnnotations(annotations);
    }

    // <startExpr> + <countExpr> without a model; with one, the literal sum of the two constants
    private static bool TryBuildBoundExpression(
        ExpressionSyntax startExpr,
        ExpressionSyntax countExpr,
        SemanticModel? semanticModel,
        [NotNullWhen(true)] out ExpressionSyntax? boundExpr)
    {
        boundExpr = null;
        if (semanticModel == null)
        {
            // <startExpr> + <countExpr>
            boundExpr = SyntaxFactory.BinaryExpression(
                SyntaxKind.AddExpression,
                startExpr,
                countExpr
            );
            return true;
        }

        if (!GpuCodeConstraints.TryGetMacroConstantInt(startExpr, semanticModel, out var startValue) ||
            !GpuCodeConstraints.TryGetMacroConstantInt(countExpr, semanticModel, out var countValue))
        {
            return false;
        }

        var sum = (long)startValue + countValue;
        if (sum is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        var bound = (int)sum;
        boundExpr = bound >= 0
            ? SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(bound)
            )
            : SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.UnaryMinusExpression,
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(-bound)
                )
            );
        return true;
    }
}
