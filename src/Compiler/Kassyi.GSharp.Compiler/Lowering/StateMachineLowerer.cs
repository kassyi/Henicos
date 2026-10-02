namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Phase 2: Lowers C# AST into a flat state machine separated by Gpu.SyncThreads().
/// </summary>
public class StateMachineLowerer
{
    public BlockSyntax Lower(BlockSyntax block)
    {
        var statements = block.Statements;
        var cases = ImmutableArray.CreateBuilder<SwitchSectionSyntax>();
        var currentCaseStatements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var state = 0;

        foreach (var stmt in statements)
        {
            if (IsSyncThreads(stmt))
            {
                // state update and return
                currentCaseStatements.Add(SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("_state"),
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state + 1))
                    )
                ));
                currentCaseStatements.Add(SyntaxFactory.ReturnStatement());

                // create case
                cases.Add(SyntaxFactory.SwitchSection(
                    SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.CaseSwitchLabel(
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
                    )),
                    SyntaxFactory.List(currentCaseStatements)
                ));

                currentCaseStatements.Clear();
                state++;
            }
            else
            {
                currentCaseStatements.Add(stmt);
            }
        }

        // Final case (state = -1 to indicate completion)
        currentCaseStatements.Add(SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("_state"),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
            )
        ));
        currentCaseStatements.Add(SyntaxFactory.ReturnStatement());

        cases.Add(SyntaxFactory.SwitchSection(
            SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.CaseSwitchLabel(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
            )),
            SyntaxFactory.List(currentCaseStatements)
        ));

        var switchStmt = SyntaxFactory.SwitchStatement(
            SyntaxFactory.IdentifierName("_state"),
            SyntaxFactory.List(cases)
        );

        return SyntaxFactory.Block(switchStmt);
    }

    private static bool IsSyncThreads(StatementSyntax stmt)
    {
        if (stmt is not ExpressionStatementSyntax exprStmt ||
            exprStmt.Expression is not InvocationExpressionSyntax inv)
        {
            return false;
        }

        if (inv.Expression is MemberAccessExpressionSyntax ma)
        {
            return ma.Name.Identifier.ValueText == "SyncThreads" && 
                   (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText == "Gpu";
        }
        if (inv.Expression is IdentifierNameSyntax id)
        {
            return id.Identifier.ValueText == "SyncThreads";
        }
        return false;
    }
}
