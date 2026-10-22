namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles <c>Gpu.SyncGrid</c> blocks, managing nested synchronization depths and inserting cooperative grid sync calls.
/// </summary>
internal sealed class SyncGridMacroRule : IMacroTranspilerRule
{
    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        IMethodSymbol symbol,
        ref TranspileContext context)
    {
        if (symbol.Name != TypeConstants.GpuMacros.SyncGrid || symbol.ContainingType.Name != TypeConstants.GpuType ||
            symbol.Parameters.Length != 1)
        {
            return false;
        }

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, invocation,
                "Gpu.SyncGrid requires a single lambda expression argument");
            return false;
        }

        switch (lambdaExpr.Body)
        {
            case BlockSyntax block:
                var flatStatements = AutoSyncGrid.Flatten(block.Statements, context.Query.SemanticModel);
                if (MacroTranspilerHelper.HasExecutableStatements(flatStatements))
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
        var isNested = context.Emitter.AutoSyncGridDepth > 0;
        context.Emitter.AutoSyncGridDepth++;
        try
        {
            using (isNested ? default : context.Emitter.Writer.Scope())
            {
                if (flatStatements != null)
                {
                    MacroTranspilerHelper.EmitFlattenedSyncGridBlock(flatStatements, ref context);
                }
                else if (expr != null)
                {
                    context.Visitor.Visit(expr);
                    context.Emitter.Writer.AppendSemiColonLine();
                }
            }
        }
        finally
        {
            context.Emitter.AutoSyncGridDepth--;
        }
    }
}
