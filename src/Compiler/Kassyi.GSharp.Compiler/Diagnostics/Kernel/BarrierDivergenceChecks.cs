namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Where a barrier stands: divergence (GSH0037), <c>fixed</c> (GSH0043), <c>using</c> scopes (GSH0003) and local functions (GSH0045) (<see cref="KernelBarrierValidator"/>).</summary>
internal static class BarrierDivergenceChecks
{
    /// <summary>
    /// Validates immediate syntactic ancestors of a barrier invocation (GSH0037, GSH0043, GSH0045, and GSH0003 for a
    /// barrier in the scope of a <c>using</c>).
    /// </summary>
    /// <param name="scan">The state of the run.</param>
    /// <param name="barrier">A block barrier, or a call to a device function that reaches a barrier.</param>
    /// <param name="isBlockBarrier">Whether every thread of the block has to reach <paramref name="barrier"/> (GSH0037).</param>
    internal static void InspectBarrierAncestors(KernelBarrierScan scan, InvocationExpressionSyntax barrier, bool isBlockBarrier)
    {
        SyntaxNode previous = barrier;
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != scan.Method; ancestor = ancestor.Parent)
        {
            var child = previous;
            previous = ancestor;

            // GSH0037: only the threads for which a thread-variant condition holds reach the barrier
            if (isBlockBarrier && GetDivergingCondition(ancestor, child) is { } condition && scan.Variance.IsThreadVariant(condition))
            {
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.DivergentBarrierProhibited,
                    barrier.GetLocation());
            }

            switch (ancestor)
            {
                // GSH0043: fixed block barrier prohibited
                case FixedStatementSyntax:
                    scan.Context.ReportDiagnostic(
                        GSharpDiagnostics.FixedBlockBarrierProhibited,
                        barrier.GetLocation());
                    break;
                // GSH0003: the state machine would leave the scope of the resource at the barrier and dispose it
                case UsingStatementSyntax:
                case BlockSyntax block when IsInUsingDeclarationScope(block, barrier):
                    scan.Context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedSyntax,
                        barrier.GetLocation(),
                        GpuCodeConstraints.BarrierInUsingScope);
                    break;
                // GSH0045: Queue local function containing barrier for recursion check
                case LocalFunctionStatementSyntax localFunc:
                    {
                        if (!scan.LocalFunctionsWithBarrier.Contains(localFunc))
                        {
                            scan.LocalFunctionsWithBarrier.Add(localFunc);
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// The condition that decides whether <paramref name="child"/> of <paramref name="ancestor"/> runs: a branch, the
    /// body of a loop, or an operand that is evaluated only for some values of another operand. GSH0077 decides with
    /// it which dynamic <c>SharedMemory&lt;T&gt;</c> declarations only some threads reach.
    /// </summary>
    internal static ExpressionSyntax? GetDivergingCondition(SyntaxNode ancestor, SyntaxNode child) => ancestor switch
    {
        IfStatementSyntax ifStatement when child != ifStatement.Condition => ifStatement.Condition,
        SwitchStatementSyntax switchStatement when child != switchStatement.Expression => switchStatement.Expression,
        WhileStatementSyntax whileStatement when child != whileStatement.Condition => whileStatement.Condition,
        DoStatementSyntax doStatement when child != doStatement.Condition => doStatement.Condition,
        ForStatementSyntax { Condition: { } condition } forStatement when child == forStatement.Statement => condition,
        ForEachStatementSyntax forEach when child == forEach.Statement => forEach.Expression,
        ConditionalExpressionSyntax conditional when child != conditional.Condition => conditional.Condition,
        BinaryExpressionSyntax binary when child == binary.Right &&
                                           binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
                                               SyntaxKind.CoalesceExpression) => binary.Left,
        _ => null
    };

    /// <summary>
    /// Whether <paramref name="barrier"/> in <paramref name="block"/> is in the scope of a <c>using</c> declaration of the
    /// block: one declared before it, or any one when the block is a nested block, whose statements the lowering moves
    /// into the enclosing block (BranchRewriter, LoopRewriter) so that its scope would end there.
    /// </summary>
    private static bool IsInUsingDeclarationScope(BlockSyntax block, InvocationExpressionSyntax barrier)
    {
        var isNested = block.Parent is BlockSyntax;
        return block.Statements
            .AsStackEnumerable()
            .Any((barrier.SpanStart, isNested), static (statement, state) =>
                UsingTranspiler.IsUsingDeclaration(statement) &&
                (state.isNested || statement.SpanStart < state.SpanStart));
    }
}
