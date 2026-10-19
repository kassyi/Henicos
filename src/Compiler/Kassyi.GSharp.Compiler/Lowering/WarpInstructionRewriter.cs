namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Hoists nested warp-level primitives into standalone variable declarations for state machine compatibility.</summary>
public class WarpInstructionRewriter : CSharpSyntaxRewriter
{
    private int _tempCounter;

    /// <summary>Rewrites expressions containing warp primitives by hoisting them into prior statements.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) => (BlockSyntax)Visit(block);

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(node.Statements.Count);

        foreach (var statement in node.Statements)
        {
            var hoistedAssignments = ImmutableArray.CreateBuilder<StatementSyntax>();

            // Rewrite the statement and capture any hoisted warp instructions
            var rewrittenStatement = new StatementRewriter(this, hoistedAssignments).Visit(statement);

            newStatements.AddRange(hoistedAssignments);
            newStatements.Add((StatementSyntax)rewrittenStatement);
        }

        return node.WithStatements(SyntaxFactory.List(newStatements));
    }

    private string GetNextTempName() => $"{GSharpInternalNames.WarpTempPrefix}{_tempCounter++}";

    private class StatementRewriter(
        WarpInstructionRewriter parent,
        ImmutableArray<StatementSyntax>.Builder hoistedAssignments)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // First, visit children to handle nested warp instructions (e.g., WarpShuffle(WarpShuffle(x)))
            var visited = base.VisitInvocationExpression(node);
            if (visited is not InvocationExpressionSyntax visitedNode)
            {
                return visited;
            }

            if (IsWarpPrimitive(visitedNode))
            {
                switch (node.Parent)
                {
                    // If it's already a statement, no need to hoist (e.g. SyncWarp or a standalone WarpShuffle)
                    case ExpressionStatementSyntax:
                    // If it's already perfectly hoisted, skip
                    case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd } when
                        vd.Identifier.Text.StartsWith(GSharpInternalNames.WarpTempPrefix, StringComparison.Ordinal):

                        return visitedNode;
                }

                var tempName = parent.GetNextTempName();

                // Create: var <warp temp> = <invocation>;
                var declaration = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("var"),
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(
                                SyntaxFactory.Identifier(tempName)
                            ).WithInitializer(
                                SyntaxFactory.EqualsValueClause(visitedNode)
                            )
                        )
                    )
                ).NormalizeWhitespace();

                hoistedAssignments.Add(declaration);

                return SyntaxFactory.IdentifierName(tempName).WithTriviaFrom(visitedNode);
            }

            return visitedNode;
        }

        private static bool IsWarpPrimitive(InvocationExpressionSyntax inv) => BarrierHelper.IsWarpShuffle(inv);
    }
}

