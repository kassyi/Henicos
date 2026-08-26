namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

internal sealed class GpuMacroArgumentRule
{
    public static readonly GpuMacroArgumentRule Instance = new();

    private GpuMacroArgumentRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        var semanticModel = context.SemanticModel;
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol invocationSymbol)
        {
            return;
        }

        if (invocationSymbol.ContainingType?.Name != "Gpu")
        {
            return;
        }

        if (invocationSymbol.Name == "ForUnroll")
        {
            var hasContext = invocationSymbol.Parameters.Length == 4;
            var offset = hasContext ? 1 : 0;
            var args = invocation.ArgumentList.Arguments;
            if (args.Count >= offset + 2)
            {
                CheckConstant(args[offset].Expression, "start", "ForUnroll", ref context);
                CheckConstant(args[offset + 1].Expression, "count", "ForUnroll", ref context);
            }
        }
        else if (invocationSymbol.Name == "InlinePtx")
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count >= 1)
            {
                CheckStringLiteral(args[0].Expression, ref context);
            }
        }
    }

    private void CheckStringLiteral(ExpressionSyntax expr, ref ValidationContext context)
    {
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return;
        }

        if (expr is InterpolatedStringExpressionSyntax)
        {
            return;
        }

        var constant = context.SemanticModel.GetConstantValue(expr);
        if (constant.HasValue && constant.Value is string)
        {
            return;
        }

        var diagnostic = Diagnostic.Create(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, expr.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private void CheckConstant(ExpressionSyntax expr, string paramName, string macroName, ref ValidationContext context)
    {
        var constant = context.SemanticModel.GetConstantValue(expr);
        if (!constant.HasValue)
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.MacroArgumentMustBeConstant,
                expr.GetLocation(),
                paramName,
                macroName);
            context.ReportDiagnostic(diagnostic);
        }
    }
}

