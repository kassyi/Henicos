namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Emits the statements of a <c>Gpu.SyncGrid(() =&gt; { ... })</c> body with a cooperative grid sync inserted between consecutive statements.
/// </summary>
/// <remarks>
/// Nested <c>Gpu.SyncGrid</c> lambdas are flattened into the enclosing statement list, and explicit parameterless
/// <c>Gpu.SyncGrid()</c> calls suppress the automatic sync next to them. Active while <see cref="Emitter.AutoSyncGridDepth"/> is positive.
/// </remarks>
internal static class AutoSyncGrid
{
    /// <summary>
    /// Emits the statements of <paramref name="node"/> in auto-sync mode.
    /// </summary>
    public static void VisitBlock(BlockSyntax node, ref TranspileContext context)
    {
        var flatStatements = Flatten(node.Statements, context.Query.SemanticModel);
        VisitStatements(flatStatements, ref context);
    }

    /// <summary>
    /// Emits already flattened statements, inserting <c>cg::this_grid().sync();</c> where required.
    /// </summary>
    public static void VisitStatements(IReadOnlyList<CSharpSyntaxNode> statements, ref TranspileContext context)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            context.Visitor.Visit(statements[i]);
            context.Emitter.Writer.AppendLine();
            if (ShouldInsertAutoSync(statements, i, ref context))
            {
                context.Emitter.Writer.AppendLine("cg::this_grid().sync();");
            }
        }
    }

    /// <summary>
    /// Flattens nested <c>Gpu.SyncGrid(lambda)</c> statements into a single statement list.
    /// </summary>
    public static IReadOnlyList<CSharpSyntaxNode> Flatten(
        IReadOnlyList<StatementSyntax> statements,
        SemanticModel semanticModel)
    {
        var result = ImmutableArray.CreateBuilder<CSharpSyntaxNode>(statements.Count);
        FlattenInto(statements, semanticModel, result);
        return result.ToImmutable();
    }

    private static bool ShouldInsertAutoSync(
        IReadOnlyList<CSharpSyntaxNode> statements,
        int currentIndex,
        ref TranspileContext context)
    {
        if (context.Emitter.AutoSyncGridDepth <= 0 || currentIndex >= statements.Count - 1)
        {
            return false;
        }

        var semanticModel = context.Query.SemanticModel;
        var current = statements[currentIndex];
        if (IsEmptyOrSyncGrid(current, semanticModel))
        {
            return false;
        }

        for (var nextIdx = currentIndex + 1; nextIdx < statements.Count; nextIdx++)
        {
            var next = statements[nextIdx];
            if (next is EmptyStatementSyntax)
            {
                continue;
            }

            return !IsParameterlessSyncGrid(next, semanticModel);
        }

        return false;
    }

    private static bool IsEmptyOrSyncGrid(CSharpSyntaxNode stmt, SemanticModel semanticModel) =>
        stmt is EmptyStatementSyntax || IsParameterlessSyncGrid(stmt, semanticModel);

    private static bool IsParameterlessSyncGrid(CSharpSyntaxNode stmt, SemanticModel semanticModel)
    {
        var invocation = stmt switch
        {
            ExpressionStatementSyntax exprStmt => exprStmt.Expression as InvocationExpressionSyntax,
            InvocationExpressionSyntax inv => inv,
            _ => null
        };

        if (invocation == null)
        {
            return false;
        }

        return semanticModel.GetSymbol(invocation) is IMethodSymbol symbol &&
               GpuMacroClassifier.Classify(symbol) is { Kind: GpuMacroKind.SyncGrid, HasBody: false };
    }

    private static void FlattenInto(
        IEnumerable<StatementSyntax> statements,
        SemanticModel semanticModel,
        ImmutableArray<CSharpSyntaxNode>.Builder result)
    {
        foreach (var stmt in statements)
        {
            if (TryGetSyncGridLambdaBody(stmt, semanticModel, out var body))
            {
                switch (body)
                {
                    case BlockSyntax innerBlock:
                        FlattenInto(innerBlock.Statements, semanticModel, result);
                        break;
                    case ExpressionSyntax innerExpr:
                        result.Add(innerExpr);
                        break;
                }
            }
            else
            {
                result.Add(stmt);
            }
        }
    }

    private static bool TryGetSyncGridLambdaBody(
        StatementSyntax stmt,
        SemanticModel semanticModel,
        out CSharpSyntaxNode? body)
    {
        body = null;
        if (stmt is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
        {
            return false;
        }

        if (semanticModel.GetSymbol(invocation) is not IMethodSymbol symbol ||
            GpuMacroClassifier.Classify(symbol) is not { Kind: GpuMacroKind.SyncGrid, HasBody: true } ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        body = lambda.Body;
        return true;
    }
}
