namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Provides shared helper utilities and scope managers for GPU macro transpilation rules.
/// </summary>
internal static class MacroTranspilerHelper
{
    /// <summary>
    /// Manages scoped variable overrides for expanded GPU macro iterations and lambda parameters.
    /// </summary>
    public readonly ref struct MacroScope
    {
        private readonly OverrideScopeStack _stack;
        public readonly string ParamName;
        private readonly int _cookie;

        public MacroScope(
            ref TranspileContext context,
            string paramName,
            string? contextParamName,
            ExpressionSyntax? contextArgExpr)
        {
            _stack = context.Emitter.LocalVariableOverrides;
            ParamName = paramName;
            _cookie = _stack.Count;

            if (contextParamName != null && contextArgExpr != null)
            {
                context.Emitter.PushOverrideScope(contextParamName, new OverrideValue(contextArgExpr));
            }
        }

        public void Dispose() => _stack.Pop(_cookie);
    }

    public static bool TryBeginMacroScope(
        InvocationExpressionSyntax invocation,
        LambdaExpressionSyntax lambdaExpr,
        bool hasContext,
        SeparatedSyntaxList<ArgumentSyntax> args,
        ref TranspileContext context,
        string macroName,
        out MacroScope scope)
    {
        scope = default;
        var paramName = ExtractUnrollLoopParameterName(lambdaExpr, hasContext);
        if (string.IsNullOrEmpty(paramName))
        {
            context.Reporter.Report(GSharpDiagnostics.MacroLambdaRequiresParameter, invocation, macroName);
            return false;
        }

        string? contextParamName = null;
        ExpressionSyntax? contextArgExpr = null;
        if (hasContext && lambdaExpr is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            contextParamName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            contextArgExpr = args[0].Expression;
        }

        scope = new MacroScope(ref context, paramName, contextParamName, contextArgExpr);
        return true;
    }

    public static void EmitUnrolledBody(ref TranspileContext context, SyntaxNode rewrittenBody)
    {
        using (context.Emitter.Writer.Scope())
        {
            switch (rewrittenBody)
            {
                case BlockSyntax block:
                    context.Visitor.VisitBlock(block);
                    break;

                case ExpressionSyntax expr:
                    context.Visitor.Visit(expr);
                    context.Emitter.Writer.AppendSemiColonLine();
                    break;
            }
        }
    }

    public static bool TryGetConstantInt(ExpressionSyntax expression, SemanticModel semanticModel, out int value)
    {
        var constant = semanticModel.GetSafeConstantValue(expression);
        if (constant.Value is int intValue)
        {
            value = intValue;
            return true;
        }

        value = 0;
        return false;
    }

    public static string ExtractUnrollLoopParameterName(LambdaExpressionSyntax lambda, bool hasContext)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax parenLambda when hasContext &&
                                                                 parenLambda.ParameterList.Parameters.Count == 2 =>
                parenLambda.ParameterList.Parameters[1].Identifier.Text,
            ParenthesizedLambdaExpressionSyntax parenLambda when !hasContext &&
                                                                 parenLambda.ParameterList.Parameters.Count == 1 =>
                parenLambda.ParameterList.Parameters[0].Identifier.Text,
            _ => string.Empty
        };
    }

    public static bool HasExecutableStatements(IReadOnlyList<CSharpSyntaxNode> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not EmptyStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    public static void EmitFlattenedSyncGridBlock(IReadOnlyList<CSharpSyntaxNode> flatStatements, ref TranspileContext context) =>
        context.Visitor.VisitStatementsWithAutoSync(flatStatements);
}
