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
        var isFullUnroll = args.Count == 0;

        if (args.Count >= 1)
        {
            var factorExpr = args[0].Expression;
            var constant = context.SemanticModel.GetConstantValue(factorExpr);
            if (!constant.HasValue || constant.Value is not int intVal || intVal < 0)
            {
                var diagnostic = Diagnostic.Create(
                    GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant,
                    factorExpr.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }

        if (invocation.Parent is not ExpressionStatementSyntax exprStmt)
        {
            return;
        }

        var loopStmt = GetSupportedLoop(exprStmt);
        if (loopStmt == null)
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.UnrollHintMustPrecedeLoop,
                invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
        else
        {
            if (isFullUnroll)
            {
                CheckStaticDeterminism(loopStmt, ref context);
            }

            CheckDynamicControlFlow(loopStmt, ref context);
        }
    }

    public static StatementSyntax? GetSupportedLoop(ExpressionStatementSyntax node)
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
                return null;
        }

        var index = statements.IndexOf(node);
        if (index < 0 || index + 1 >= statements.Count)
        {
            return null;
        }

        var nextStmt = statements[index + 1];
        return nextStmt is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax ? nextStmt : null;
    }

    private void CheckStaticDeterminism(StatementSyntax loopStmt, ref ValidationContext context)
    {
        var condition = loopStmt switch
        {
            ForStatementSyntax forStmt => forStmt.Condition,
            WhileStatementSyntax whileStmt => whileStmt.Condition,
            DoStatementSyntax doStmt => doStmt.Condition,
            _ => null,
        };

        switch (condition)
        {
            case null:
                ReportGsh0032(loopStmt, ref context);
                return;
            case BinaryExpressionSyntax binaryExpr:
            {
                var leftIsConst = IsStaticallyDeterministic(binaryExpr.Left, ref context);
                var rightIsConst = IsStaticallyDeterministic(binaryExpr.Right, ref context);
                if (!leftIsConst && !rightIsConst)
                {
                    ReportGsh0032(loopStmt, ref context);
                }

                break;
            }
            default:
            {
                if (!IsStaticallyDeterministic(condition, ref context))
                {
                    ReportGsh0032(loopStmt, ref context);
                }

                break;
            }
        }
    }

    private bool IsStaticallyDeterministic(ExpressionSyntax expr, ref ValidationContext context)
    {
        var constant = context.SemanticModel.GetConstantValue(expr);
        if (constant.HasValue)
        {
            return true;
        }

        switch (expr)
        {
            case IdentifierNameSyntax idName:
            {
                var symbol = context.SemanticModel.GetSymbolInfo(idName).Symbol;
                if (symbol is IParameterSymbol paramSymbol && paramSymbol.HasAttribute(KnownAttributes.Constant))
                {
                    return true;
                }

                break;
            }
            case BinaryExpressionSyntax binaryExpr:
                return IsStaticallyDeterministic(binaryExpr.Left, ref context) &&
                       IsStaticallyDeterministic(binaryExpr.Right, ref context);
            case ParenthesizedExpressionSyntax parenExpr:
                return IsStaticallyDeterministic(parenExpr.Expression, ref context);
        }

        return false;
    }

    private void ReportGsh0032(StatementSyntax loopStmt, ref ValidationContext context)
    {
        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.UnrolledLoopMustBeStatic,
            loopStmt.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private void CheckDynamicControlFlow(StatementSyntax loopStmt, ref ValidationContext context)
    {
        var hasDynamicControlFlow = false;
        foreach (var n in loopStmt.DescendantNodes())
        {
            if (n is BreakStatementSyntax or ReturnStatementSyntax or GotoStatementSyntax)
            {
                hasDynamicControlFlow = true;
                break;
            }
        }

        if (!hasDynamicControlFlow)
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            GSharpDiagnostics.UnrolledLoopMayBeIgnored,
            loopStmt.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private void CheckStringLiteral(ExpressionSyntax expr, ref ValidationContext context)
    {
        if ((expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) ||
            expr is InterpolatedStringExpressionSyntax ||
            context.SemanticModel.GetConstantValue(expr) is { HasValue: true, Value: string })
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
