namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Macros;

/// <summary>
/// Checks the unroll macros: the factor of <c>Gpu.HintUnroll</c> and the loop it precedes, and the constant bounds of
/// <c>Gpu.ForUnroll</c>.
/// </summary>
internal static class UnrollChecks
{
    /// <summary>
    /// Reports a <c>start</c> or <c>count</c> argument of <c>Gpu.ForUnroll</c> that is not a constant.
    /// </summary>
    public static void CheckForUnrollBounds(
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        ref ValidationContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (call.GetArgument(args, call.ValueOrdinal)?.Expression is { } start)
        {
            CheckConstant(start, "start", TypeConstants.GpuMacros.ForUnroll, ref context);
        }

        if (call.GetArgument(args, call.ValueOrdinal + 1)?.Expression is { } count)
        {
            CheckConstant(count, "count", TypeConstants.GpuMacros.ForUnroll, ref context);
        }
    }

    /// <summary>
    /// Reports a <c>Gpu.HintUnroll</c> factor that is not a non-negative constant, a hint that does not precede a loop,
    /// a fully unrolled loop whose trip count is not static, and a loop whose control flow may stop the unrolling.
    /// </summary>
    public static void CheckUnrollHint(InvocationExpressionSyntax invocation, ref ValidationContext context)
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
            return;
        }

        if (isFullUnroll && !HasStaticCondition(loopStmt, context.SemanticModel))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnrolledLoopMustBeStatic, loopStmt);
        }

        if (HasDynamicControlFlow(loopStmt))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnrolledLoopMayBeIgnored, loopStmt);
        }
    }

    /// <summary>
    /// Whether <paramref name="loopStmt"/> contains dynamic control flow that leaves an unrolled iteration.
    /// </summary>
    internal static bool HasDynamicControlFlow(SyntaxNode loopStmt) =>
        HasDynamicControlFlow(
            loopStmt,
            loopRoot: loopStmt,
            isRoot: true,
            insideNestedLoop: false,
            insideNestedSwitch: false);

    // A comparison is static when one side is; any other condition must be static as a whole
    private static bool HasStaticCondition(StatementSyntax loopStmt, SemanticModel semanticModel)
    {
        var condition = loopStmt switch
        {
            ForStatementSyntax forStmt => forStmt.Condition,
            WhileStatementSyntax whileStmt => whileStmt.Condition,
            DoStatementSyntax doStmt => doStmt.Condition,
            _ => null,
        };

        return condition switch
        {
            null => false,
            BinaryExpressionSyntax binaryExpr =>
                IsStaticallyDeterministic(binaryExpr.Left, semanticModel) ||
                IsStaticallyDeterministic(binaryExpr.Right, semanticModel),
            _ => IsStaticallyDeterministic(condition, semanticModel)
        };
    }

    // A constant, a [GpuConstexpr] parameter, or a binary expression of those, possibly in parentheses
    private static bool IsStaticallyDeterministic(ExpressionSyntax expr, SemanticModel semanticModel) =>
        semanticModel.GetSafeConstantValue(expr).HasValue ||
        expr switch
        {
            IdentifierNameSyntax idName =>
                semanticModel.GetSymbol(idName) is IParameterSymbol paramSymbol && paramSymbol.IsConstexprParameter(),
            BinaryExpressionSyntax binaryExpr =>
                IsStaticallyDeterministic(binaryExpr.Left, semanticModel) &&
                IsStaticallyDeterministic(binaryExpr.Right, semanticModel),
            ParenthesizedExpressionSyntax parenExpr => IsStaticallyDeterministic(parenExpr.Expression, semanticModel),
            _ => false
        };

    private static bool HasDynamicControlFlow(
        SyntaxNode node,
        SyntaxNode loopRoot,
        bool isRoot,
        bool insideNestedLoop,
        bool insideNestedSwitch)
    {
        if (!isRoot && LeavesIteration(node, loopRoot, insideNestedLoop, insideNestedSwitch) is { } leaves)
        {
            return leaves;
        }

        var nextInsideLoop = insideNestedLoop || (!isRoot && IsLoop(node));
        var nextInsideSwitch = insideNestedSwitch || (!isRoot && IsSwitch(node));

        return node.ChildNodes()
            .AsStackEnumerable()
            .Any(
                (Root: loopRoot, Loop: nextInsideLoop, Switch: nextInsideSwitch),
                static (child, inside) => HasDynamicControlFlow(
                    child,
                    loopRoot: inside.Root,
                    isRoot: false,
                    insideNestedLoop: inside.Loop,
                    insideNestedSwitch: inside.Switch
                )
            );
    }

    // Whether a jump leaves the unrolled iteration; null when the children decide (not a jump, not a nested function)
    private static bool? LeavesIteration(
        SyntaxNode node,
        SyntaxNode loopRoot,
        bool insideNestedLoop,
        bool insideNestedSwitch) =>
        node switch
        {
            LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax => false,
            ReturnStatementSyntax or GotoStatementSyntax => true,
            BreakStatementSyntax breakStmt => LabeledJumpSyntax.GetLabelName(breakStmt) != null
                ? LabeledJumpSyntax.FindEnclosingTarget(breakStmt) is not { } target ||
                  target == loopRoot ||
                  !loopRoot.FullSpan.Contains(target.FullSpan)
                : !insideNestedLoop && !insideNestedSwitch,
            ContinueStatementSyntax continueStmt => LabeledJumpSyntax.GetLabelName(continueStmt) != null
                ? LabeledJumpSyntax.FindEnclosingTarget(continueStmt) is not { } target ||
                  target == loopRoot ||
                  !loopRoot.FullSpan.Contains(target.FullSpan)
                : !insideNestedLoop,
            _ => null
        };

    private static bool IsLoop(SyntaxNode node) =>
        node is ForStatementSyntax or
                ForEachStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax;

    private static bool IsSwitch(SyntaxNode node) =>
        node is SwitchStatementSyntax or SwitchExpressionSyntax;

    private static void CheckConstant(
        ExpressionSyntax expr,
        string paramName,
        string macroName,
        ref ValidationContext context)
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
