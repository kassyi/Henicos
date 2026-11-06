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

        /// <summary>The loop parameter, whose references alone the iteration value replaces.</summary>
        public readonly IParameterSymbol? ParamSymbol;

        private readonly int _cookie;

        public MacroScope(
            ref TranspileContext context,
            ParameterSyntax param,
            ParameterSyntax? contextParam,
            ExpressionSyntax? contextArgExpr)
        {
            var model = context.Query.SemanticModel;
            _stack = context.Emitter.LocalVariableOverrides;
            ParamName = param.Identifier.Text;
            ParamSymbol = model.GetDeclaredSymbol<IParameterSymbol>(param);
            _cookie = _stack.Count;

            if (contextParam != null && contextArgExpr != null)
            {
                context.Emitter.PushOverrideScope(
                    contextParam.Identifier.Text,
                    new OverrideValue(contextArgExpr),
                    model.GetDeclaredSymbol<IParameterSymbol>(contextParam)
                );
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
        if (GpuCodeConstraints.GetMacroLoopParameter(lambdaExpr, hasContext) is not { Identifier.Text.Length: > 0 } param)
        {
            context.Reporter.Fail(GSharpDiagnostics.MacroLambdaRequiresParameter, invocation, macroName);
            return false;
        }

        ParameterSyntax? contextParam = null;
        ExpressionSyntax? contextArgExpr = null;
        if (hasContext && lambdaExpr is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            contextParam = parenLambda.ParameterList.Parameters[0];
            contextArgExpr = args[0].Expression;
        }

        scope = new MacroScope(ref context, param, contextParam, contextArgExpr);
        return true;
    }

    public static void EmitUnrolledBody(ref TranspileContext context, SyntaxNode rewrittenBody)
    {
        using (context.Emitter.Writer.Scope())
        {
            switch (rewrittenBody)
            {
                case BlockSyntax block:
                    context.Visitor.Visit(block);
                    break;

                case ExpressionSyntax expr:
                    context.Visitor.Visit(expr);
                    context.Emitter.Writer.AppendLine(";");
                    break;
            }
        }
    }

}
