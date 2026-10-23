namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.SyncGrid</c> blocks, managing nested synchronization depths and inserting cooperative grid sync calls.
/// </summary>
internal sealed class SyncGridMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call is not { Kind: GpuMacroKind.SyncGrid, HasBody: true })
        {
            return false;
        }

        if (!GpuCodeConstraints.TryGetSyncGridBody(invocation, out var lambdaExpr))
        {
            var args = invocation.ArgumentList.Arguments;
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, args.Count > 0 ? args[0] : invocation,
                GpuCodeConstraints.SyncGridRequiresLambda);
            return false;
        }

        switch (lambdaExpr.Body)
        {
            case BlockSyntax block:
                var flatStatements = AutoSyncGrid.Flatten(block.Statements, context.Query.SemanticModel);
                if (AutoSyncGrid.HasExecutableStatements(flatStatements))
                {
                    EmitSyncGridBody(ref context, flatStatements, expr: null);
                }

                // [WHY] Completely eliminate empty Gpu.SyncGrid(() => {}) as a No-op (zero CUDA C++ output).
                return true;

            case ExpressionSyntax expr:
                EmitSyncGridBody(ref context, flatStatements: null, expr);
                return true;

            default:
                return false;
        }
    }

    private static void EmitSyncGridBody(
        ref TranspileContext context,
        IReadOnlyList<CSharpSyntaxNode>? flatStatements,
        ExpressionSyntax? expr)
    {
        var previousDepth = AutoSyncGrid.Enter(ref context);
        try
        {
            // A nested body joins the statement list of the enclosing one, so it opens no scope of its own
            using (previousDepth > 0 ? default : context.Emitter.Writer.Scope())
            {
                if (flatStatements != null)
                {
                    AutoSyncGrid.VisitStatements(flatStatements, ref context);
                }
                else if (expr != null)
                {
                    context.Visitor.Visit(expr);
                    context.Emitter.Writer.AppendLine(";");
                }
            }
        }
        finally
        {
            AutoSyncGrid.Restore(ref context, previousDepth);
        }
    }
}
