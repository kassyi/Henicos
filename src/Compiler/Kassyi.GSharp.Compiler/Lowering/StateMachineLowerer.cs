namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Lowers flat C# AST statements into a switch-based state machine separated by thread barriers.</summary>
/// <param name="isDeviceFunction">
/// Whether the body belongs to a device function. Its end completes the call (a plain <c>return</c>, which the emitters
/// turn into "reset the frame and report completion") instead of finishing the thread.
/// </param>
public class StateMachineLowerer(bool isDeviceFunction = false)
{
    private const string WarpShuffleRead = "__gsharp_warp_shuffle_read";

    private readonly ImmutableArray<InvocationExpressionSyntax>.Builder _callSites =
        ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

    /// <summary>The calls that may suspend, in frame index order (see <see cref="LoweredStateMachine.CallSites"/>).</summary>
    public IReadOnlyList<InvocationExpressionSyntax> CallSites => _callSites.ToImmutable();

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
                if (BarrierHelper.IsSuspendingCall(barrierInv))
                {
                    // The call gets a state of its own: every resume runs it again until the callee completes
                    commitState(state + 1, isBarrier: false);
                    currentCaseStatements.Add(CreateResumeStatement(stmt, barrierInv, state));
                }
                else if (BarrierHelper.IsWarpShuffle(barrierInv))
                {
                    // 1. Publish val to the context's warp exchange slot (__gsharp_warp_exchange_assign(val))
                    var valArg = barrierInv.ArgumentList.Arguments[1].Expression;
                    currentCaseStatements.Add(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.IdentifierName(GSharpInternalNames.Prelude.WarpExchangeAssign),
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(valArg))
                                )
                            )
                        )
                    );

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

                    if (name == TypeConstants.GpuMacros.SyncGrid)
                    {
                        // __gsharp_yielded_for_grid_sync = true;
                        currentCaseStatements.Add(
                            SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.YieldedForGridSync),
                                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                                )
                            )
                        );
                    }

                    commitState(state + 1, isBarrier: true);
                }
            }
            else
            {
                currentCaseStatements.Add(RewriteGoto(stmt, labelStates));
            }
        }

        if (isDeviceFunction)
        {
            // Falling off the end completes the call
            currentCaseStatements.Add(SyntaxFactory.ReturnStatement());
            commitSection();
        }
        else
        {
            // Final state: -1
            commitState(-1, isBarrier: true);
        }

        // switch (__gsharp_state) { case 0: { ... } case 1: { ... } ... }
        var switchStmt = SyntaxFactory.SwitchStatement(
            SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State),
            SyntaxFactory.List(cases)
        );

        return SyntaxFactory.Block(switchStmt);

        void commitState(int nextState, bool isBarrier)
        {
            if (isBarrier)
            {
                // __gsharp_state = <next state>; return;
                currentCaseStatements.Add(CreateStateAssignment(nextState));
                currentCaseStatements.Add(CreateSuspendingReturn());
            }
            else
            {
                // Unconditional jump to next state within the switch
                currentCaseStatements.Add(SyntaxFactory.ParseStatement($"goto case {nextState};")
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
            }

            commitSection();
            state = nextState;
        }

        void commitSection()
        {
            // case <state>: { <statements> }
            var label = SyntaxFactory.CaseSwitchLabel(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
            );
            cases.Add(
                SyntaxFactory.SwitchSection(
                    SyntaxFactory.SingletonList<SwitchLabelSyntax>(label),
                    SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(currentCaseStatements))
                )
            );

            currentCaseStatements.Clear();
        }
    }

    // Emits: if (!__gsharp_resume(<call>, <frame index>[, <result target>])) { __gsharp_state = <resume state>; return; }
    private StatementSyntax CreateResumeStatement(StatementSyntax statement, InvocationExpressionSyntax call,
        int resumeState)
    {
        var resultTarget = statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax } => null,
            ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax { Left: var left } assignment
            } when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) => left,
            _ => throw new InvalidOperationException(
                "A call that may suspend must be lowered to a statement or a simple assignment before state machine lowering.")
        };

        var frameIndex = _callSites.Count;
        _callSites.Add(call);

        var frameIndexLiteral =
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(frameIndex));
        var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>()
            .Add(SyntaxFactory.Argument(call))
            .Add(SyntaxFactory.Argument(frameIndexLiteral));
        if (resultTarget != null)
        {
            arguments = arguments.Add(SyntaxFactory.Argument(resultTarget));
        }

        // __gsharp_resume(<call>, <frame index>[, <result target>])
        var resume = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.ResumeCall),
            SyntaxFactory.ArgumentList(arguments)
        );

        return SyntaxFactory.IfStatement(
            SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, resume),
            SyntaxFactory.Block(
                CreateStateAssignment(resumeState),
                CreateSuspendingReturn()
            )
        );
    }

    // return; marked so that the emitters treat it as a suspension, not as a return of the user's code
    private static ReturnStatementSyntax CreateSuspendingReturn() =>
        SyntaxFactory.ReturnStatement().WithAdditionalAnnotations(
            new SyntaxAnnotation(LoweringAnnotations.StateMachineReturnAnnotationKind)
        );

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

    // __gsharp_state = <stateValue>;
    private static StatementSyntax CreateStateAssignment(int stateValue)
    {
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State),
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
            "ShuffleDownSync" => GSharpInternalNames.Prelude.WarpShuffleReadDownSync,
            "ShuffleUpSync" => GSharpInternalNames.Prelude.WarpShuffleReadUpSync,
            "ShuffleXorSync" => GSharpInternalNames.Prelude.WarpShuffleReadXorSync,
            "ShuffleSync" => GSharpInternalNames.Prelude.WarpShuffleReadSync,
            _ => WarpShuffleRead
        };

        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>();
        // Arguments are: context, mask, val, delta, width [optional]
        // The root context owns the warp exchange, also when the shuffle is in a device function
        newArgs = newArgs.Add(
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Root)));
        newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[0]);
        newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[1]); // pass val for type inference
        newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[2]);
        if (barrierInv.ArgumentList.Arguments.Count > 3)
        {
            newArgs = newArgs.Add(barrierInv.ArgumentList.Arguments[3]);
        }

        // __gsharp_warp_shuffle_read_<kind>(__gsharp_root, mask, val, delta[, width])
        var rewrittenInv = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(readMethodName),
            SyntaxFactory.ArgumentList(newArgs)
        );

        return stmt.ReplaceNode(barrierInv, rewrittenInv);
    }
}

