namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Rewrites every <c>using</c> declaration of a block into a <c>using</c> statement over the rest of its block, in a
/// body lowered for the CPU stack models (<see cref="AstLoweringPipeline.LowerForStack"/>).
/// </summary>
/// <remarks>
/// [WHY] C# defines a <c>using</c> declaration as the <c>using</c> statement over the rest of its block (Roslyn lowers
/// the same way: <c>LocalRewriter_Block.VisitPossibleUsingDeclaration</c>), and C# forbids a <c>goto</c> across the
/// declaration (CS8648 / CS8649), so no label of the user changes its reachability; the gotos that the reconvergence
/// regions add then target a label after the <c>using</c> statement and never cross a declaration (C#) or enter a
/// <c>try</c> (C++).
/// </remarks>
internal sealed class UsingDeclarationScopes : CSharpSyntaxRewriter, IBlockLoweringPass
{
    /// <inheritdoc />
    public BlockSyntax Rewrite(BlockSyntax block) =>
        Visit(block) as BlockSyntax
        ?? throw new InvalidOperationException("Rewriting using declarations removed the block.");

    public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

    public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

    public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

    public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = base.VisitBlock(node) as BlockSyntax ?? node;
        return visited.WithStatements(Wrap(visited.Statements, 0));
    }

    // { a; using var x = e; b; c; } -> { a; using (var x = e) { b; c; } }
    private static SyntaxList<StatementSyntax> Wrap(SyntaxList<StatementSyntax> statements, int start)
    {
        for (var i = start; i < statements.Count; i++)
        {
            if (!UsingCpuEmitter.TryGetUsingDeclaration(statements[i], out var declaration))
            {
                continue;
            }

            var rest = Wrap(statements, i + 1);
            var builder = ImmutableArray.CreateBuilder<StatementSyntax>(i - start + 1);
            for (var k = start; k < i; k++)
            {
                builder.Add(statements[k]);
            }

            builder.Add(ToUsingStatement(statements[i], declaration, SyntaxFactory.Block(rest)));
            return SyntaxFactory.List(builder.MoveToImmutable());
        }

        if (start == 0)
        {
            return statements;
        }

        var tail = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count - start);
        for (var k = start; k < statements.Count; k++)
        {
            tail.Add(statements[k]);
        }

        return SyntaxFactory.List(tail.MoveToImmutable());
    }

    // using (<declaration>) { <rest> }
    private static StatementSyntax ToUsingStatement(
        StatementSyntax statement,
        LocalDeclarationStatementSyntax declaration,
        BlockSyntax body) =>
        statement is LabeledStatementSyntax labeled
            ? labeled.WithStatement(ToUsingStatement(labeled.Statement, declaration, body))
            : SyntaxFactory.UsingStatement(
                    declaration.Declaration.WithoutTrivia(),
                    null,
                    body
                )
                .WithOriginalSpanFrom(declaration);
}
