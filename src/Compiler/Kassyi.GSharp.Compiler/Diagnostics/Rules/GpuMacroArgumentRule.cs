namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that verifies required arguments to GPU macro methods are compile-time constants.</summary>
internal sealed class GpuMacroArgumentRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly GpuMacroArgumentRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral,
        GSharpDiagnostics.MacroArgumentMustBeConstant,
        GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant,
        GSharpDiagnostics.UnrollHintMustPrecedeLoop,
        GSharpDiagnostics.UnrolledLoopMustBeStatic,
        GSharpDiagnostics.UnrolledLoopMayBeIgnored,
        GSharpDiagnostics.UnsupportedSyntax,
        GSharpDiagnostics.SyncGridRequiresCooperativeKernel);

    private GpuMacroArgumentRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        var semanticModel = context.SemanticModel;
        if (semanticModel.GetSymbol(invocation) is not IMethodSymbol invocationSymbol)
        {
            return;
        }

        var containingType = invocationSymbol.ContainingType;
        if (containingType is { Name: TypeConstants.GpuMacros.Hint, ContainingType.Name: TypeConstants.GpuMacros.Gpu })
        {
            AnalyzeHintMacro(invocation, invocationSymbol, ref context);
            return;
        }

        if (containingType?.Name == TypeConstants.GpuMacros.Gpu)
        {
            AnalyzeGpuMacro(invocation, invocationSymbol, ref context);
        }
    }

    private void AnalyzeHintMacro(InvocationExpressionSyntax invocation, IMethodSymbol invocationSymbol,
        ref ValidationContext context)
    {
        if (invocationSymbol.Name == TypeConstants.GpuMacros.Unroll)
        {
            CheckUnrollHint(invocation, ref context);
        }
    }

    private void AnalyzeGpuMacro(InvocationExpressionSyntax invocation, IMethodSymbol invocationSymbol,
        ref ValidationContext context)
    {
        switch (invocationSymbol.Name)
        {
            case TypeConstants.GpuMacros.ForUnroll:
                CheckForUnroll(invocation, invocationSymbol, ref context);
                break;

            case TypeConstants.GpuMacros.InlinePtx:
                CheckInlinePtx(invocation, ref context);
                break;

            case TypeConstants.GpuMacros.SyncGrid:
                CheckSyncGrid(invocation, invocationSymbol, ref context);
                break;
        }
    }

    private void CheckForUnroll(InvocationExpressionSyntax invocation, IMethodSymbol invocationSymbol,
        ref ValidationContext context)
    {
        var hasContext = invocationSymbol.Parameters.Length == 4;
        var offset = hasContext ? 1 : 0;
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < offset + 2)
        {
            return;
        }

        CheckConstant(args[offset].Expression, "start", TypeConstants.GpuMacros.ForUnroll, ref context);
        CheckConstant(args[offset + 1].Expression, "count", TypeConstants.GpuMacros.ForUnroll, ref context);
    }

    private void CheckInlinePtx(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count >= 1)
        {
            CheckStringLiteral(args[0].Expression, ref context);
        }
    }

    private void CheckSyncGrid(InvocationExpressionSyntax invocation, IMethodSymbol invocationSymbol,
        ref ValidationContext context)
    {
        if (invocationSymbol.Parameters.Length == 1)
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count >= 1 && args[0].Expression is not LambdaExpressionSyntax)
            {
                context.Report(
                    GSharpDiagnostics.UnsupportedSyntax,
                    args[0],
                    "Gpu.SyncGrid requires a lambda expression argument");
            }
        }

        if (context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
            !IsCooperativeKernel(context.MethodSymbol))
        {
            context.Report(
                GSharpDiagnostics.SyncGridRequiresCooperativeKernel,
                invocation,
                context.MethodSymbol.Name);
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
                context.Report(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant, factorExpr);
            }
        }

        if (invocation.Parent is not ExpressionStatementSyntax exprStmt)
        {
            return;
        }

        var loopStmt = GetSupportedLoop(exprStmt);
        if (loopStmt == null)
        {
            context.Report(GSharpDiagnostics.UnrollHintMustPrecedeLoop, invocation);
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
        while (true)
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
                        var symbol = context.SemanticModel.GetSymbol(idName);
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
                    expr = parenExpr.Expression;
                    continue;
            }

            return false;
        }
    }

    private void ReportGsh0032(StatementSyntax loopStmt, ref ValidationContext context) =>
        context.Report(GSharpDiagnostics.UnrolledLoopMustBeStatic, loopStmt);

    private void CheckDynamicControlFlow(StatementSyntax loopStmt, ref ValidationContext context)
    {
        var hasDynamicControlFlow = false;
        foreach (var n in loopStmt.DescendantNodes())
        {
            if (n is not (BreakStatementSyntax or ReturnStatementSyntax or GotoStatementSyntax))
            {
                continue;
            }

            hasDynamicControlFlow = true;
            break;
        }

        if (!hasDynamicControlFlow)
        {
            return;
        }

        context.Report(GSharpDiagnostics.UnrolledLoopMayBeIgnored, loopStmt);
    }

    private void CheckStringLiteral(ExpressionSyntax expr, ref ValidationContext context)
    {
        if ((expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) ||
            expr is InterpolatedStringExpressionSyntax ||
            context.SemanticModel.GetConstantValue(expr) is { HasValue: true, Value: string })
        {
            return;
        }

        context.Report(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, expr);
    }

    private void CheckConstant(ExpressionSyntax expr, string paramName, string macroName, ref ValidationContext context)
    {
        var constant = context.SemanticModel.GetConstantValue(expr);
        if (constant.HasValue)
        {
            return;
        }

        context.Report(
            GSharpDiagnostics.MacroArgumentMustBeConstant,
            expr,
            paramName,
            macroName);
    }

    private static bool IsCooperativeKernel(IMethodSymbol methodSymbol)
    {
        var kernelAttr = methodSymbol.GetAttribute(KnownAttributes.GpuKernel);
        if (kernelAttr == null)
        {
            return false;
        }

        foreach (var namedArg in kernelAttr.NamedArguments)
        {
            if (namedArg is { Key: "Cooperative", Value.Value: true })
            {
                return true;
            }
        }

        return false;
    }
}
