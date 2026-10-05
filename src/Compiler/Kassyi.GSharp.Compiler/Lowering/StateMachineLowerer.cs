namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Lowers flat C# AST statements into a switch-based state machine separated by thread barriers.</summary>
public class StateMachineLowerer
{
    /// <summary>Transforms statements into switch sections keyed by state indices across barrier boundaries.</summary>
    public BlockSyntax Lower(BlockSyntax block)
    {
        var statements = block.Statements;
        var cases = ImmutableArray.CreateBuilder<SwitchSectionSyntax>();
        var currentCaseStatements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var state = 0;

        foreach (var stmt in statements)
        {
            var barrierInv = BarrierHelper.GetPhaseBarrierInvocation(stmt);
            if (barrierInv != null)
            {
                if (BarrierHelper.IsWarpShuffle(barrierInv))
                {
                    // 1. _ctx->_warpExchange = val; (__gsharp_warp_exchange_assign(val))
                    var valArg = barrierInv.ArgumentList.Arguments[1].Expression;
                    currentCaseStatements.Add(SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("__gsharp_warp_exchange_assign"),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(valArg)))
                        )
                    ));

                    // 2. State Split & Return
                    commitState(state + 1);

                    // 3. Rewrite original Warp.ShuffleXXX(...) to __gsharp_warp_shuffle_read_XXX(...)
                    currentCaseStatements.Add(RewriteWarpShuffleRead(stmt, barrierInv));
                }
                else
                {
                    // Regular SyncThreads
                    commitState(state + 1);
                }
            }
            else
            {
                currentCaseStatements.Add(stmt);
            }
        }

        // [WHY] State -1 signals the CPU emulator that this thread has completed its execution.
        commitState(-1);

        var switchStmt = SyntaxFactory.SwitchStatement(
            SyntaxFactory.IdentifierName("_state"),
            SyntaxFactory.List(cases)
        );

        return SyntaxFactory.Block(switchStmt);

        void commitState(int nextState)
        {
            currentCaseStatements.Add(CreateStateAssignment(nextState));
            currentCaseStatements.Add(SyntaxFactory.ReturnStatement()
                .WithAdditionalAnnotations(new SyntaxAnnotation("StateMachineReturn")));

            cases.Add(SyntaxFactory.SwitchSection(
                SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.CaseSwitchLabel(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
                )),
                SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(currentCaseStatements))
            ));

            currentCaseStatements.Clear();
            state++;
        }
    }

    private static StatementSyntax CreateStateAssignment(int stateValue)
    {
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("_state"),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(stateValue))
            )
        );
    }

    private static StatementSyntax RewriteWarpShuffleRead(StatementSyntax stmt, InvocationExpressionSyntax barrierInv)
    {
        var methodName = (barrierInv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? "ShuffleDownSync";
        var readMethodName = methodName switch
        {
            "ShuffleDownSync" => "__gsharp_warp_shuffle_read_down_sync",
            "ShuffleUpSync" => "__gsharp_warp_shuffle_read_up_sync",
            "ShuffleXorSync" => "__gsharp_warp_shuffle_read_xor_sync",
            "ShuffleSync" => "__gsharp_warp_shuffle_read_sync",
            _ => "__gsharp_warp_shuffle_read"
        };

        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>();
        // Arguments are: _ctx, mask, val, delta, width [optional]
        newArgs = newArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("_ctx")));
        newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[0]);
        newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[1]); // pass val for type inference
        newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[2]);
        if (barrierInv.ArgumentList.Arguments.Count > 3)
        {
            newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[3]);
        }

        var rewrittenInv = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(readMethodName),
            SyntaxFactory.ArgumentList(newArgs)
        );

        return stmt.ReplaceNode(barrierInv, rewrittenInv);
    }
}

