namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Diagnostic rule that verifies required arguments to GPU macro methods are compile-time constants.</summary>
internal sealed class GpuMacroArgumentRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly GpuMacroArgumentRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.InlinePtxNotSupportedByDialect,
        GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral,
        GSharpDiagnostics.MacroArgumentMustBeConstant,
        GSharpDiagnostics.MacroLambdaRequiresParameter,
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
                CheckLoopBody(invocation, call, TypeConstants.GpuMacros.ForUnroll, ref context);
                break;

            case GpuMacroKind.GridStrideLoop:
                CheckLoopBody(invocation, call, TypeConstants.GpuMacros.GridStrideLoop, ref context);
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

    private static void CheckLoopBody(InvocationExpressionSyntax invocation, GpuMacroCall call, string macroName,
        ref ValidationContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count <= call.LambdaArgumentIndex ||
            args[call.LambdaArgumentIndex].Expression is not LambdaExpressionSyntax body)
        {
            return;
        }

        if (GpuCodeConstraints.GetMacroLoopParameter(body, call.HasContext) is not { } loopParameter)
        {
            context.ReportDiagnostic(GSharpDiagnostics.MacroLambdaRequiresParameter, invocation, macroName);
            return;
        }

        CheckLoopBodyReturn(body, macroName, ref context);
        CheckLoopVariableUse(body, loopParameter, macroName, ref context);
    }

    // [WHY] In C# a return leaves the lambda, which is the end of one iteration. The transpiler writes the body into
    // the surrounding code, where it would end the kernel, so the two would silently differ
    private static void CheckLoopBodyReturn(
        LambdaExpressionSyntax body,
        string macroName,
        ref ValidationContext context)
    {
        // A return in a lambda or local function of the body leaves that function, not the loop body
        var statements = body.Body.DescendantNodesAndSelf(
            static node => node is not (
                LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax));
        foreach (var node in statements)
        {
            if (node is ReturnStatementSyntax)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    $"return in a Gpu.{macroName} body");
            }
        }
    }

    // [WHY] In C# the loop variable is a parameter, so assigning it or taking its address is legal. The transpiler
    // replaces it by the iteration index, which is a value and has no address
    private static void CheckLoopVariableUse(
        LambdaExpressionSyntax body,
        ParameterSyntax loopParameter,
        string macroName,
        ref ValidationContext context)
    {
        var semanticModel = context.SemanticModel;
        var loopSymbol = semanticModel.GetDeclaredSymbol<IParameterSymbol>(loopParameter)?.OriginalDefinition;
        if (loopSymbol == null)
        {
            return;
        }

        var name = loopParameter.Identifier.ValueText;
        foreach (var node in body.Body.DescendantNodesAndSelf())
        {
            var isWrite = node switch
            {
                AssignmentExpressionSyntax assignment =>
                    IsLoopVariable(assignment.Left, semanticModel, loopSymbol),
                PrefixUnaryExpressionSyntax unary
                    when unary.IsKind(SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression) =>
                    IsLoopVariable(unary.Operand, semanticModel, loopSymbol),
                PostfixUnaryExpressionSyntax unary
                    when unary.IsKind(SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression) =>
                    IsLoopVariable(unary.Operand, semanticModel, loopSymbol),
                ArgumentSyntax argument
                    when argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword) =>
                    IsLoopVariable(argument.Expression, semanticModel, loopSymbol),
                _ => false
            };
            var isReference = node switch
            {
                PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.AddressOfExpression) =>
                    IsLoopVariable(unary.Operand, semanticModel, loopSymbol),
                ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword) =>
                    IsLoopVariable(argument.Expression, semanticModel, loopSymbol),
                _ => false
            };
            if (isWrite || isReference)
            {
                var kind = isWrite ? "assignment to" : "reference to";
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    $"{kind} the Gpu.{macroName} loop variable '{name}'");
            }
        }
    }

    // The loop variable, possibly in parentheses or among the targets of a deconstruction
    private static bool IsLoopVariable(ExpressionSyntax expression, SemanticModel semanticModel, ISymbol loopSymbol) =>
        expression switch
        {
            IdentifierNameSyntax name =>
                semanticModel.GetSymbol(name)?.OriginalDefinition.IsEqualTo(loopSymbol) == true,
            ParenthesizedExpressionSyntax parenthesized =>
                IsLoopVariable(parenthesized.Expression, semanticModel, loopSymbol),
            TupleExpressionSyntax tuple => tuple.Arguments
                .AsStackEnumerable()
                .Any(
                    (semanticModel, loopSymbol),
                    static (argument, state) =>
                        IsLoopVariable(argument.Expression, state.semanticModel, state.loopSymbol)
                ),
            _ => false
        };

    private void CheckInlinePtx(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        if (!GpuCodeConstraints.SupportsInlinePtx(context.Dialect))
        {
            context.ReportDiagnostic(GSharpDiagnostics.InlinePtxNotSupportedByDialect, invocation, context.Dialect.Name);
        }

        // [WHY] Reported for every dialect: the text must be a constant even where the call becomes a runtime failure
        var args = invocation.ArgumentList.Arguments;
        if (args.Count >= 1)
        {
            CheckStringLiteral(args[0].Expression, ref context);
        }
    }

    private void CheckSyncGrid(InvocationExpressionSyntax invocation, GpuMacroCall call, ref ValidationContext context)
    {
        if (call.HasBody && !GpuCodeConstraints.TryGetSyncGridBody(invocation, out _))
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count >= 1)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    args[0],
                    GpuCodeConstraints.SyncGridRequiresLambda);
            }
        }

        if (context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
            !context.MethodSymbol.IsCooperativeKernel())
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
            if (!GpuCodeConstraints.TryGetUnrollFactor(factorExpr, context.SemanticModel, out _))
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant, factorExpr);
            }
        }

        if (invocation.Parent is not ExpressionStatementSyntax exprStmt)
        {
            return;
        }

        var loopStmt = GpuCodeConstraints.GetUnrolledLoop(exprStmt);
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
                        if (symbol is IParameterSymbol paramSymbol && paramSymbol.IsConstexprParameter())
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

        return node.ChildNodes()
            .AsStackEnumerable()
            .Any(
                (Loop: nextInsideLoop, Switch: nextInsideSwitch),
                static (child, inside) => HasDynamicControlFlow(
                    child,
                    isRoot: false,
                    insideNestedLoop: inside.Loop,
                    insideNestedSwitch: inside.Switch
                )
            );
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
        if (GpuCodeConstraints.IsInlinePtxText(expr, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, expr);
    }

    private void CheckConstant(ExpressionSyntax expr, string paramName, string macroName, ref ValidationContext context)
    {
        if (GpuCodeConstraints.TryGetMacroConstantInt(expr, context.SemanticModel, out _))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.MacroArgumentMustBeConstant,
            expr,
            paramName,
            macroName);
    }
}
