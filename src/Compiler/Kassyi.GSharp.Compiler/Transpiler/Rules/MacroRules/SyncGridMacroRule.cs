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
        if (symbol.Name != "SyncGrid" || symbol.ContainingType.Name != TypeConstants.GpuType ||
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

        return lambdaExpr.Body switch
        {
            BlockSyntax block => TranspileBlock(block, ref context),
            ExpressionSyntax expr => TranspileExpression(expr, ref context),
            _ => false
        };
    }

    private static bool TranspileBlock(BlockSyntax block, ref TranspileContext context)
    {
        var flatStatements = RoslynToCudaVisitor.FlattenStatements(block.Statements, context.Query.SemanticModel);
        if (!MacroTranspilerHelper.HasExecutableStatements(flatStatements))
        {
            // [WHY] Completely eliminate empty Gpu.SyncGrid(() => {}) as a No-op (zero CUDA C++ output).
            return true;
        }

        var isNested = context.Emitter.AutoSyncGridDepth > 0;
        context.Emitter.AutoSyncGridDepth++;
        try
        {
            if (isNested)
            {
                MacroTranspilerHelper.EmitFlattenedSyncGridBlock(flatStatements, ref context);
            }
            else
            {
                using (context.Emitter.Writer.Scope())
                {
                    MacroTranspilerHelper.EmitFlattenedSyncGridBlock(flatStatements, ref context);
                }
            }
        }
        finally
        {
            context.Emitter.AutoSyncGridDepth--;
        }

        return true;
    }

    private static bool TranspileExpression(ExpressionSyntax expr, ref TranspileContext context)
    {
        var isNested = context.Emitter.AutoSyncGridDepth > 0;
        context.Emitter.AutoSyncGridDepth++;
        try
        {
            if (isNested)
            {
                context.Visitor.Visit(expr);
                context.Emitter.Writer.AppendSemiColonLine();
            }
            else
            {
                using (context.Emitter.Writer.Scope())
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

        return true;
    }
}
