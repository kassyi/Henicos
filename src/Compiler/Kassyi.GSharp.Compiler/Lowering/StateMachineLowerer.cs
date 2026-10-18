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

        // 1. Pass 1: Identify all state boundaries (barriers AND labels) and assign sequential states.
        var labelStates = new Dictionary<string, int>();
        int simulatedState = 0;
        foreach (var stmt in statements)
        {
            if (stmt is LabeledStatementSyntax labeled)
            {
                simulatedState++;
                labelStates[labeled.Identifier.ValueText] = simulatedState;
            }

            if (BarrierHelper.GetPhaseBarrierInvocation(stmt) != null)
            {
                simulatedState++;
            }
        }

        // 2. Pass 2: Generate switch cases.
        foreach (var stmt in statements)
        {
            if (stmt is LabeledStatementSyntax labeled)
            {
                // A label boundary requires falling through to the next state case without yielding.
                commitState(state + 1, isBarrier: false);

                if (labeled.Statement is not EmptyStatementSyntax)
                {
                    currentCaseStatements.Add(RewriteGoto(labeled.Statement, labelStates));
                }

                continue;
            }

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
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(valArg)))
                        )
                    ));

                    // 2. State Split & Return
                    commitState(state + 1, isBarrier: true);

                    // 3. Rewrite original Warp.ShuffleXXX(...) to __gsharp_warp_shuffle_read_XXX(...)
                    var readStmt = RewriteWarpShuffleRead(stmt, barrierInv);
                    currentCaseStatements.Add(RewriteGoto(readStmt, labelStates));
                }
                else
                {
                    var name = (barrierInv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText
                               ?? (barrierInv.Expression as IdentifierNameSyntax)?.Identifier.ValueText;

                    if (name == "SyncGrid")
                    {
                        currentCaseStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName("_yieldedForGridSync"),
                                SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                            )
                        ));
                    }

                    commitState(state + 1, isBarrier: true);
                }
            }
            else
            {
                currentCaseStatements.Add(RewriteGoto(stmt, labelStates));
            }
        }

        // Final state: -1
        commitState(-1, isBarrier: true);

        var switchStmt = SyntaxFactory.SwitchStatement(
            SyntaxFactory.IdentifierName("_state"),
            SyntaxFactory.List(cases)
        );

        return SyntaxFactory.Block(switchStmt);

        void commitState(int nextState, bool isBarrier)
        {
            if (isBarrier)
            {
                currentCaseStatements.Add(CreateStateAssignment(nextState));
                currentCaseStatements.Add(SyntaxFactory.ReturnStatement()
                    .WithAdditionalAnnotations(new SyntaxAnnotation("StateMachineReturn")));
            }
            else
            {
                // Unconditional jump to next state within the switch
                currentCaseStatements.Add(SyntaxFactory.ParseStatement($"goto case {nextState};")
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
            }

            cases.Add(SyntaxFactory.SwitchSection(
                SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.CaseSwitchLabel(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
                )),
                SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(currentCaseStatements))
            ));

            currentCaseStatements.Clear();
            state = nextState;
        }
    }

    private static StatementSyntax RewriteGoto(StatementSyntax stmt, Dictionary<string, int> labelStates) =>
        (StatementSyntax)new GotoRewriter(labelStates).Visit(stmt);

    private class GotoRewriter(Dictionary<string, int> labelStates) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
        {
            if (!node.IsKind(SyntaxKind.GotoStatement) || node.Expression is not IdentifierNameSyntax id)
            {
                return base.VisitGotoStatement(node);
            }

            if (labelStates.TryGetValue(id.Identifier.ValueText, out var targetState))
            {
                return SyntaxFactory.ParseStatement($"goto case {targetState};")
                    .WithTriviaFrom(node);
            }

            return base.VisitGotoStatement(node);
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
        var methodName = (barrierInv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ??
                         "ShuffleDownSync";
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

