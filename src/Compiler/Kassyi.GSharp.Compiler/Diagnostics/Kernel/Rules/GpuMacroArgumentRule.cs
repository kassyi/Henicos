namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

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

        var call = GpuMacroClassifier.Classify(invocationSymbol);
        switch (call.Kind)
        {
            case GpuMacroKind.HintUnroll:
                CheckUnrollHint(invocation, ref context);
                break;

            case GpuMacroKind.ForUnroll:
                CheckForUnroll(invocation, call, ref context);
                break;

            case GpuMacroKind.InlinePtx:
                CheckInlinePtx(invocation, ref context);
                break;

            case GpuMacroKind.SyncGrid:
                CheckSyncGrid(invocation, call, ref context);
                break;
        }
    }

    private void CheckForUnroll(InvocationExpressionSyntax invocation, GpuMacroCall call, ref ValidationContext context)
    {
        var offset = call.ArgumentOffset;
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

    private void CheckSyncGrid(InvocationExpressionSyntax invocation, GpuMacroCall call, ref ValidationContext context)
    {
        if (call.HasBody)
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count >= 1 && args[0].Expression is not LambdaExpressionSyntax)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    args[0],
                    "Gpu.SyncGrid requires a lambda expression argument");
            }
        }

        if (context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
            !IsCooperativeKernel(context.MethodSymbol))
        {
            context.ReportDiagnostic(
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
            var constant = context.SemanticModel.GetSafeConstantValue(factorExpr);
            if (!constant.HasValue || constant.Value is not int intVal || intVal < 0)
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant, factorExpr);
            }
        }

        if (invocation.Parent is not ExpressionStatementSyntax exprStmt)
        {
            return;
        }

        var loopStmt = GetSupportedLoop(exprStmt);
        if (loopStmt == null)
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnrollHintMustPrecedeLoop, invocation);
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
            var constant = context.SemanticModel.GetSafeConstantValue(expr);
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
        context.ReportDiagnostic(GSharpDiagnostics.UnrolledLoopMustBeStatic, loopStmt);

    private void CheckDynamicControlFlow(StatementSyntax loopStmt, ref ValidationContext context)
    {
        if (HasDynamicControlFlow(loopStmt, isRoot: true, insideNestedLoop: false, insideNestedSwitch: false))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnrolledLoopMayBeIgnored, loopStmt);
        }
    }

    private static bool HasDynamicControlFlow(
        SyntaxNode node,
        bool isRoot,
        bool insideNestedLoop,
        bool insideNestedSwitch)
    {
        if (!isRoot)
        {
            if (node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }

            if (node is ReturnStatementSyntax or GotoStatementSyntax)
            {
                return true;
            }

            if (node is BreakStatementSyntax)
            {
                return !insideNestedLoop && !insideNestedSwitch;
            }

            if (node is ContinueStatementSyntax)
            {
                return !insideNestedLoop;
            }
        }

        var nextInsideLoop = insideNestedLoop || (!isRoot && IsLoop(node));
        var nextInsideSwitch = insideNestedSwitch || (!isRoot && IsSwitch(node));

        foreach (var child in node.ChildNodes())
        {
            if (HasDynamicControlFlow(child, isRoot: false, insideNestedLoop: nextInsideLoop, insideNestedSwitch: nextInsideSwitch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoop(SyntaxNode node) =>
        node is ForStatementSyntax or
                ForEachStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax;

    private static bool IsSwitch(SyntaxNode node) =>
        node is SwitchStatementSyntax or SwitchExpressionSyntax;

    private void CheckStringLiteral(ExpressionSyntax expr, ref ValidationContext context)
    {
        if ((expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) ||
            expr is InterpolatedStringExpressionSyntax ||
            context.SemanticModel.GetSafeConstantValue(expr) is { HasValue: true, Value: string })
        {
            return;
        }

        context.ReportDiagnostic(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, expr);
    }

    private void CheckConstant(ExpressionSyntax expr, string paramName, string macroName, ref ValidationContext context)
    {
        var constant = context.SemanticModel.GetSafeConstantValue(expr);
        if (constant.HasValue)
        {
            return;
        }

        context.ReportDiagnostic(
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
