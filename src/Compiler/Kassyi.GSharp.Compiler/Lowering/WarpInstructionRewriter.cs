namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Hoists nested warp-level primitives into standalone variable declarations for state machine compatibility.</summary>
public class WarpInstructionRewriter : CSharpSyntaxRewriter
{
    private int _tempCounter;

    /// <summary>Rewrites expressions containing warp primitives by hoisting them into prior statements.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) => (BlockSyntax)Visit(block);

    public override SyntaxNode VisitBlock(BlockSyntax node) => node.WithStatements(RewriteStatements(node.Statements));

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
    {
        var statements = RewriteStatements(node.Statements);

        // [WHY] A declaration directly under a case label is skipped by jumps to later labels, which C++ rejects
        return statements.Count == node.Statements.Count
            ? node.WithStatements(statements)
            : node.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(statements)));
    }

    // [WHY] A temporary lands right before its statement in the same list, so it stays in the scope of loop variables
    // and is evaluated on every iteration of the enclosing loop.
    private SyntaxList<StatementSyntax> RewriteStatements(SyntaxList<StatementSyntax> statements)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count);

        foreach (var statement in statements)
        {
            var hoistedAssignments = ImmutableArray.CreateBuilder<StatementSyntax>();

            // Rewrite the statement and capture any hoisted warp instructions
            var rewrittenStatement = new StatementRewriter(this, statement, hoistedAssignments).Visit(statement) as StatementSyntax
                                     ?? throw new InvalidOperationException("Warp instruction hoisting removed a statement.");

            newStatements.AddRange(hoistedAssignments);
            newStatements.Add(rewrittenStatement);
        }

        return SyntaxFactory.List(newStatements);
    }

    private string GetNextTempName() => $"{GSharpInternalNames.WarpTempPrefix}{_tempCounter++}";

    private static bool ContainsWarpPrimitive(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodes())
        {
            if (descendant is InvocationExpressionSyntax inv && BarrierHelper.IsWarpShuffle(inv))
            {
                return true;
            }
        }

        return false;
    }

    private class StatementRewriter(
        WarpInstructionRewriter parent,
        StatementSyntax rootStatement,
        ImmutableArray<StatementSyntax>.Builder hoistedAssignments)
        : CSharpSyntaxRewriter
    {
        // Nested statement lists hoist into their own scope instead of in front of the root statement
        public override SyntaxNode VisitBlock(BlockSyntax node) => parent.VisitBlock(node);

        public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node) => parent.VisitSwitchSection(node);

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            // An embedded statement without braces ('for (...) x += Shuffle(...);') has no list for the temporary
            if (node is StatementSyntax statement and not BlockSyntax &&
                statement != rootStatement &&
                statement.Parent is not (BlockSyntax or SwitchSectionSyntax or LabeledStatementSyntax) &&
                ContainsWarpPrimitive(statement))
            {
                return parent.VisitBlock(SyntaxFactory.Block(statement));
            }

            return base.Visit(node);
        }

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

