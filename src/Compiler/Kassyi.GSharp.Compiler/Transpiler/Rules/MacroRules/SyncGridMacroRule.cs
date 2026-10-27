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

        var statements = SyncGridBody.Flatten(lambdaExpr, context.Query.SemanticModel);

        // [WHY] Completely eliminate empty Gpu.SyncGrid(() => {}) as a No-op (zero CUDA C++ output).
        if (statements.HasExecutableStatements)
        {
            EmitSyncGridBody(ref context, statements);
        }

        return true;
    }

    private static void EmitSyncGridBody(ref TranspileContext context, SyncGridStatements statements)
    {
        var previousDepth = AutoSyncGrid.Enter(ref context);
        try
        {
            // A nested body joins the statement list of the enclosing one, so it opens no scope of its own
            using (previousDepth > 0 ? default : context.Emitter.Writer.Scope())
            {
                AutoSyncGrid.VisitStatements(statements.Elements, ref context);
            }
        }
        finally
        {
            AutoSyncGrid.Restore(ref context, previousDepth);
        }
    }
}
