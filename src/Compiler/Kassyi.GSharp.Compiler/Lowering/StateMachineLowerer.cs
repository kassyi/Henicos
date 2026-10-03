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
                // The C++ emulator relies on returning to the scheduler at each barrier.
                currentCaseStatements.Add(SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("_state"),
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state + 1))
                    )
                ));
                currentCaseStatements.Add(SyntaxFactory.ReturnStatement()
                    .WithAdditionalAnnotations(new SyntaxAnnotation("StateMachineReturn")));


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

        // State -1 signals the emulator that this kernel execution thread has reached its end.
        currentCaseStatements.Add(SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("_state"),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
            )
        ));
        currentCaseStatements.Add(SyntaxFactory.ReturnStatement()
            .WithAdditionalAnnotations(new SyntaxAnnotation("StateMachineReturn")));

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

        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText == "SyncThreads" &&
                                               (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText == "Gpu",
            IdentifierNameSyntax id => id.Identifier.ValueText == "SyncThreads",
            _ => false
        };
    }
}
