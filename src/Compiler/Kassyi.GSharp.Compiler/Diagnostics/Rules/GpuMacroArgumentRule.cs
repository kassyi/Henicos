namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that verifies required arguments to GPU macro methods are compile-time constants.</summary>
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

        var containingType = invocationSymbol.ContainingType;
        if (containingType is { Name: "Hint", ContainingType.Name: "Gpu" })
        {
            if (invocationSymbol.Name == "Unroll")
            {
                CheckUnrollHint(invocation, ref context);
            }

            return;
        }

        if (containingType?.Name != "Gpu")
        {
            return;
        }

        switch (invocationSymbol.Name)
        {
            case "ForUnroll":
            {
                var hasContext = invocationSymbol.Parameters.Length == 4;
                var offset = hasContext ? 1 : 0;
                var args = invocation.ArgumentList.Arguments;
                if (args.Count >= offset + 2)
                {
                    CheckConstant(args[offset].Expression, "start", "ForUnroll", ref context);
                    CheckConstant(args[offset + 1].Expression, "count", "ForUnroll", ref context);
                }

                break;
            }
            case "InlinePtx":
            {
                var args = invocation.ArgumentList.Arguments;
                if (args.Count >= 1)
                {
                    CheckStringLiteral(args[0].Expression, ref context);
                }

                break;
            }
        }
    }

    private void CheckUnrollHint(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        Diagnostic? diagnostic;
        if (args.Count >= 1)
        {
            var factorExpr = args[0].Expression;
            var constant = context.SemanticModel.GetConstantValue(factorExpr);
            if (!constant.HasValue || constant.Value is not int intVal || intVal < 0)
            {
                diagnostic = Diagnostic.Create(
                    GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant,
                    factorExpr.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }

        if (invocation.Parent is not ExpressionStatementSyntax exprStmt)
        {
            return;
        }


        if (PrecedesSupportedLoop(exprStmt))
        {
            return;
        }

        diagnostic = Diagnostic.Create(
            GSharpDiagnostics.UnrollHintMustPrecedeLoop,
            invocation.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    public static bool PrecedesSupportedLoop(ExpressionStatementSyntax node)
    {
        SyntaxList<StatementSyntax> statements;
        switch (node.Parent)
        {
            case BlockSyntax block:
                statements = block.Statements;
                break;
            case SwitchSectionSyntax switchSection:
                statements = switchSection.Statements;
                break;
            default:
                return false;
        }

        var index = statements.IndexOf(node);
        if (index < 0 || index + 1 >= statements.Count)
        {
            return false;
        }

        var nextStmt = statements[index + 1];
        return nextStmt is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
    }

    private void CheckStringLiteral(ExpressionSyntax expr, ref ValidationContext context)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
            case InterpolatedStringExpressionSyntax:
                return;
        }

        var constant = context.SemanticModel.GetConstantValue(expr);
        if (constant is { HasValue: true, Value: string })
        {
            return;
        }

        var diagnostic = Diagnostic.Create(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, expr.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private void CheckConstant(ExpressionSyntax expr, string paramName, string macroName, ref ValidationContext context)
    {
        var constant = context.SemanticModel.GetConstantValue(expr);
        if (constant.HasValue)
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.MacroArgumentMustBeConstant,
            expr.GetLocation(),
            paramName,
            macroName);
        context.ReportDiagnostic(diagnostic);
    }
}

