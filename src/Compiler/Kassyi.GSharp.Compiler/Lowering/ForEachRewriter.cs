namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers <c>foreach</c> statements that contain a phase barrier into <c>for</c> loops (for arrays and spans) prior to
/// <see cref="LoopRewriter"/>.
/// </summary>
public class ForEachRewriter : CSharpSyntaxRewriter
{
    private int _loopCounter;

    private static bool ContainsBarrier(SyntaxNode node) =>
        node.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && BarrierHelper.IsPhaseBarrier(inv));

    /// <summary>Rewrites <c>foreach</c> loops within <paramref name="block"/> that contain a phase barrier.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) =>
        ContainsBarrier(block) ? (BlockSyntax)Visit(block) : block;

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(node.Statements.Count);

        foreach (var statement in node.Statements)
        {
            var visited = (StatementSyntax)Visit(statement);

            if (visited is BlockSyntax nestedBlock)
            {
                if (statement is not BlockSyntax || ContainsBarrier(statement))
                {
                    newStatements.AddRange(nestedBlock.Statements);
                }
                else
                {
                    newStatements.Add(nestedBlock);
                }
            }
            else
            {
                newStatements.Add(visited);
            }
        }

        return SyntaxFactory.Block(newStatements);
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (!ContainsBarrier(node))
        {
            return base.VisitForEachStatement(node);
        }

        var id = _loopCounter++;
        var expr = ((ExpressionSyntax)Visit(node.Expression)).WithoutTrivia();
        var body = (StatementSyntax)Visit(node.Statement);

        return LowerArrayForEach(node, expr, body, id);
    }

    // {
    //     var __gsharp_foreach_src_<id> = <expr>;
    //     for (int __gsharp_foreach_i_<id> = 0;
    //          __gsharp_foreach_i_<id> < __gsharp_foreach_src_<id>.Length;
    //          __gsharp_foreach_i_<id>++)
    //     {
    //         <T> <x> = __gsharp_foreach_src_<id>[__gsharp_foreach_i_<id>];
    //         <body>
    //     }
    // }
    internal static BlockSyntax LowerArrayForEach(
        ForEachStatementSyntax node,
        ExpressionSyntax expr,
        StatementSyntax body,
        int id)
    {
        var annotations = ImmutableArray.CreateRange(
            node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
        );
        var srcName = $"{GSharpInternalNames.Lowering.ForEachSourcePrefix}{id}";
        var indexName = $"{GSharpInternalNames.Lowering.ForEachIndexPrefix}{id}";

        var srcDeclarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(srcName))
            .WithInitializer(SyntaxFactory.EqualsValueClause(expr));
        var srcStatement = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(srcDeclarator)
            )
        ).WithAdditionalAnnotations(annotations);

        var indexDeclarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(indexName))
            .WithInitializer(
                SyntaxFactory.EqualsValueClause(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))
                )
            )
            .WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.ForEachIndexAnnotationKind));
        var indexDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
            SyntaxFactory.SingletonSeparatedList(indexDeclarator)
        ).WithAdditionalAnnotations(annotations);

        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(indexName),
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(srcName)
                    .WithAdditionalAnnotations(
                        new SyntaxAnnotation(LoweringAnnotations.ForEachArraySourceAnnotationKind)),
                SyntaxFactory.IdentifierName("Length")
            )
        ).WithAdditionalAnnotations(annotations);

        var incrementor = SyntaxFactory.PostfixUnaryExpression(
            SyntaxKind.PostIncrementExpression,
            SyntaxFactory.IdentifierName(indexName)
        ).WithAdditionalAnnotations(annotations);

        var elementAccess = SyntaxFactory.ElementAccessExpression(
            SyntaxFactory.IdentifierName(srcName),
            SyntaxFactory.BracketedArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(indexName))
                )
            )
        ).WithAdditionalAnnotations(annotations);

        var itemDeclarator = SyntaxFactory.VariableDeclarator(node.Identifier.WithoutTrivia())
            .WithInitializer(SyntaxFactory.EqualsValueClause(elementAccess))
            .WithAdditionalAnnotations(annotations);
        var itemStatement = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                node.Type.WithoutTrivia(),
                SyntaxFactory.SingletonSeparatedList(itemDeclarator)
            )
        ).WithAdditionalAnnotations(annotations);

        var bodyStatements = ImmutableArray.CreateBuilder<StatementSyntax>();
        bodyStatements.Add(itemStatement);
        if (body is BlockSyntax bodyBlock)
        {
            bodyStatements.AddRange(bodyBlock.Statements);
        }
        else
        {
            bodyStatements.Add(body);
        }

        var forStatement = SyntaxFactory.ForStatement(
            indexDeclaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(incrementor),
            SyntaxFactory.Block(bodyStatements).WithAdditionalAnnotations(annotations)
        ).WithAdditionalAnnotations(annotations);

        return SyntaxFactory.Block(srcStatement, forStatement).WithAdditionalAnnotations(annotations);
    }
}
