namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Phase 2.5: Hoists Warp-level primitives (like Gpu.WarpShuffle) into pre-assignment statements.
/// This ensures they can be properly suspended/resumed by the state machine emulator.
/// </summary>
public class WarpInstructionRewriter : CSharpSyntaxRewriter
{
    private int _tempCounter;

    public BlockSyntax Rewrite(BlockSyntax block) => (BlockSyntax)Visit(block);

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = SyntaxFactory.List<StatementSyntax>();

        foreach (var statement in node.Statements)
        {
            var hoistedAssignments = ImmutableArray.CreateBuilder<StatementSyntax>();

            // Rewrite the statement and capture any hoisted warp instructions
            var rewrittenStatement = new StatementRewriter(this, hoistedAssignments).Visit(statement);

            newStatements = newStatements.AddRange(hoistedAssignments);

            newStatements = newStatements.Add((StatementSyntax)rewrittenStatement);
        }

        return node.WithStatements(newStatements);
    }

    private string GetNextTempName() => $"_warpTemp_{_tempCounter++}";

    private class StatementRewriter(
        WarpInstructionRewriter parent,
        ImmutableArray<StatementSyntax>.Builder hoistedAssignments)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // First, visit children to handle nested warp instructions (e.g., WarpShuffle(WarpShuffle(x)))
            var visitedNode = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (IsWarpPrimitive(visitedNode))
            {
                switch (node.Parent)
                {
                    // If it's already a statement, no need to hoist (e.g. SyncWarp or a standalone WarpShuffle)
                    case ExpressionStatementSyntax:
                    // If it's already perfectly hoisted, skip
                    case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd } when
                        vd.Identifier.Text.StartsWith("_warpTemp_"):

                        return visitedNode;
                }

                var tempName = parent.GetNextTempName();

                // Create: var _warpTemp_X = <invocation>;
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
