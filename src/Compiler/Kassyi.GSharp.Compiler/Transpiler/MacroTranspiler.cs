using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Microsoft.CodeAnalysis.Operations;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler;

// ReSharper disable once UnusedType.Global -- Used implicitly by Roslyn compiler infra
// ReSharper disable once UnusedMember.Global -- Used implicitly by Roslyn compiler infra
/// <summary>Expands high-level GSharp GPU macros (such as unrolled loops and grid-stride iterations) into CUDA C++ primitives.</summary>
internal static class MacroTranspiler
{
    /// <summary>
    /// Attempts to intercept and transpile a high-level GPU macro invocation or assignment expression into target dialect constructs.
    /// </summary>
    public static bool TryTranspileMacro(ExpressionStatementSyntax node, ref TranspileContext context)
    {
        return node.Expression switch
        {
            InvocationExpressionSyntax invocation => TryTranspileInvocation(invocation, node, ref context),
            AssignmentExpressionSyntax assignment => TryTranspileAssignment(assignment, ref context),
            _ => false
        };
    }

    private static bool TryTranspileInvocation(InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement, ref TranspileContext context)
    {
        if (context.Query.SemanticModel.GetSymbol(invocation) is not IMethodSymbol symbol ||
            !symbol.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        var typeName = symbol.ContainingType.Name;
        if (typeName == "Hint" && symbol.ContainingType.ContainingType?.Name == TypeConstants.GpuType)
        {
            return symbol.Name == "Unroll" && TranspileHintUnroll(invocation, statement, ref context);
        }

        if (typeName != TypeConstants.GpuType)
        {
            return false;
        }

        return symbol.Name switch
        {
            "ForUnroll" => TranspileForUnroll(invocation, ref context, symbol),
            "GridStrideLoop" => TranspileGridStrideLoop(invocation, ref context, symbol),
            "SingleThread" => TranspileSingleThread(invocation, ref context),
            "SyncGrid" when symbol.Parameters.Length == 1 => TranspileSyncGrid(invocation, ref context),
            _ => false
        };
    }

    private static bool TranspileHintUnroll(InvocationExpressionSyntax invocation, ExpressionStatementSyntax statement,
        ref TranspileContext context)
    {
        if (GpuMacroArgumentRule.GetSupportedLoop(statement) == null)
        {
            context.Reporter.Report(GSharpDiagnostics.UnrollHintMustPrecedeLoop, invocation);
            return true;
        }

        var args = invocation.ArgumentList.Arguments;
        var factor = 0;
        if (args.Count > 0)
        {
            var factorExpr = args[0].Expression;
            var constantValue = context.Query.SemanticModel.GetConstantValue(factorExpr);
            if (!constantValue.HasValue || constantValue.Value is not int intVal || intVal < 0)
            {
                context.Reporter.Report(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant, factorExpr);
                return true;
            }

            factor = intVal;
        }

        if (factor > 0)
        {
            context.Emitter.Writer.AppendLine($"#pragma unroll {factor}");
        }
        else
        {
            context.Emitter.Writer.AppendLine("#pragma unroll");
        }

        return true;
    }

    private static bool TranspileForUnroll(InvocationExpressionSyntax invocation, ref TranspileContext context,
        IMethodSymbol symbol)
    {
        var hasContext = symbol.Parameters.Length == 4;
        var offset = hasContext ? 1 : 0;
        var args = invocation.ArgumentList.Arguments;

        if (args.Count < offset + 3 || args[offset + 2].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        if (!TryGetConstantInt(args[offset].Expression, context.Query.SemanticModel, out var start) ||
            !TryGetConstantInt(args[offset + 1].Expression, context.Query.SemanticModel, out var count))
        {
            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, invocation, "ForUnroll arguments must be constants");
            return false;
        }

        if (!TryBeginMacroScope(invocation, lambdaExpr, hasContext, args, ref context, "ForUnroll", out var macroScope))
        {
            return false;
        }

        using var scopeDisposer = macroScope;

        context.Emitter.Writer.AppendLine($"// [GpuUnroll] start={start}, count={count}");

        // [WHY] Unrolls the loop at C# source transpilation time by substituting the loop induction variable with literal constants, eliminating runtime branching in the GPU kernel.
        for (var i = start; i < start + count; i++)
        {
            var iterationCookie = context.Emitter.PushOverrideScope(macroScope.ParamName, new OverrideValue(i.ToString()));
            try
            {
                EmitUnrolledBody(ref context, lambdaExpr.Body);
            }
            finally
            {
                context.Emitter.PopOverrideScope(iterationCookie);
            }
        }

        return true;
    }

    private static bool TranspileGridStrideLoop(InvocationExpressionSyntax invocation, ref TranspileContext context,
        IMethodSymbol symbol)
    {
        var hasContext = symbol.Parameters.Length == 3;
        var offset = hasContext ? 1 : 0;
        var args = invocation.ArgumentList.Arguments;

        if (args.Count < offset + 2 || args[offset + 1].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        if (!TryBeginMacroScope(invocation, lambdaExpr, hasContext, args, ref context, "GridStrideLoop",
                out var macroScope))
        {
            return false;
        }

        using var scopeDisposer = macroScope;

        // Emit: for (int {paramName} = blockIdx.x * blockDim.x + threadIdx.x; {paramName} < [EXPR]; {paramName} += gridDim.x * blockDim.x)
        context.Emitter.Writer.AppendKeyword("for");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append(
                $"int {macroScope.ParamName} = blockIdx.x * blockDim.x + threadIdx.x; {macroScope.ParamName} < ");
            context.Visitor.Visit(args[offset].Expression);
            context.Emitter.Writer.Append($"; {macroScope.ParamName} += gridDim.x * blockDim.x");
        }

        context.Emitter.Writer.AppendLine();
        EmitUnrolledBody(ref context, lambdaExpr.Body);

        return true;
    }

    private readonly ref struct MacroScope
    {
        private readonly OverrideScopeStack _stack;
        public readonly string ParamName;
        private readonly int _cookie;

        public MacroScope(ref TranspileContext context, string paramName, string? contextParamName,
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

        public void Dispose()
        {
            _stack.Pop(_cookie);
        }
    }

    private static bool TryBeginMacroScope(
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

    private static bool TranspileSingleThread(InvocationExpressionSyntax invocation, ref TranspileContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        context.Emitter.Writer.AppendKeyword("if");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("threadIdx.x == 0 && blockIdx.x == 0");
        }

        context.Emitter.Writer.AppendLine();

        // Suppress AutoSyncGrid in SingleThread blocks to prevent GPU deadlock (grid sync requires all threads)
        var prevDepth = context.Emitter.AutoSyncGridDepth;
        context.Emitter.AutoSyncGridDepth = 0;
        try
        {
            EmitUnrolledBody(ref context, lambdaExpr.Body);
        }
        finally
        {
            context.Emitter.AutoSyncGridDepth = prevDepth;
        }

        return true;
    }

    private static bool TranspileSyncGrid(InvocationExpressionSyntax invocation, ref TranspileContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, invocation, "Gpu.SyncGrid requires a single lambda expression argument");
            return false;
        }

        if (lambdaExpr.Body is BlockSyntax block)
        {
            var flatStatements = RoslynToCudaVisitor.FlattenStatements(block.Statements, context.Query.SemanticModel);
            if (!HasExecutableStatements(flatStatements))
            {
                return true; // [WHY] Completely eliminate empty Gpu.SyncGrid(() => {}) as a No-op (zero CUDA C++ output).
            }

            var isNested = context.Emitter.AutoSyncGridDepth > 0;
            context.Emitter.AutoSyncGridDepth++;
            try
            {
                if (isNested)
                {
                    EmitFlattenedSyncGridBlock(flatStatements, ref context);
                }
                else
                {
                    using (context.Emitter.Writer.Scope())
                    {
                        EmitFlattenedSyncGridBlock(flatStatements, ref context);
                    }
                }
            }
            finally
            {
                context.Emitter.AutoSyncGridDepth--;
            }

            return true;
        }

        if (lambdaExpr.Body is ExpressionSyntax expr)
        {
            var isNested = context.Emitter.AutoSyncGridDepth > 0;
            context.Emitter.AutoSyncGridDepth++;
            try
            {
                if (isNested)
                {
                    context.Visitor.Visit(expr);
                    context.Emitter.Writer.AppendSemiColonLine();
                }
                else
                {
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Visitor.Visit(expr);
                        context.Emitter.Writer.AppendSemiColonLine();
                    }
                }
            }
            finally
            {
                context.Emitter.AutoSyncGridDepth--;
            }

            return true;
        }

        return false;
    }

    private static bool HasExecutableStatements(IReadOnlyList<StatementSyntax> statements)
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

    private static void EmitFlattenedSyncGridBlock(IReadOnlyList<StatementSyntax> flatStatements, ref TranspileContext context)
    {
        for (var i = 0; i < flatStatements.Count; i++)
        {
            var stmt = flatStatements[i];
            context.Visitor.Visit(stmt);
            context.Emitter.Writer.AppendLine();
            if (context.Visitor.ShouldInsertAutoSync(flatStatements, i))
            {
                context.Emitter.Writer.AppendLine("cg::this_grid().sync();");
            }
        }
    }

    private static void EmitUnrolledBody(ref TranspileContext context, SyntaxNode rewrittenBody)
    {
        using (context.Emitter.Writer.Scope())
        {
            switch (rewrittenBody)
            {
                case BlockSyntax block:
                    var flatStatements = context.Emitter.AutoSyncGridDepth > 0
                        ? RoslynToCudaVisitor.FlattenStatements(block.Statements, context.Query.SemanticModel)
                        : block.Statements;
                    for (var i = 0; i < flatStatements.Count; i++)
                    {
                        var stmt = flatStatements[i];
                        context.Visitor.Visit(stmt);
                        context.Emitter.Writer.AppendLine();
                        if (context.Visitor.ShouldInsertAutoSync(flatStatements, i))
                        {
                            context.Emitter.Writer.AppendLine("cg::this_grid().sync();");
                        }
                    }

                    break;

                case ExpressionSyntax expr:
                    context.Visitor.Visit(expr);
                    context.Emitter.Writer.AppendSemiColonLine();
                    break;
            }
        }
    }

    private static bool TryGetConstantInt(ExpressionSyntax expression, SemanticModel semanticModel, out int value)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant.Value is int intValue)
        {
            value = intValue;
            return true;
        }

        value = 0;
        return false;
    }

    private static string ExtractUnrollLoopParameterName(LambdaExpressionSyntax lambda, bool hasContext)
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

    private static bool TryTranspileAssignment(AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        var rightNode = UnwrapExpression(assignment.Right, context.Query.SemanticModel);

        if (context.Query.SemanticModel.GetSymbol(rightNode) is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        AttributeData? macroAttr = null;
        foreach (var a in methodSymbol.GetAttributes())
        {
            if (a.IsAttribute("GpuOperatorMacro") &&
                a.AttributeClass?.ContainingNamespace.IsCoreNamespace() == true)
            {
                macroAttr = a;
                break;
            }
        }

        if (macroAttr == null || macroAttr.ConstructorArguments.Length < 2)
        {
            return false;
        }

        return EmitMacroOperatorAssignment(assignment, rightNode, macroAttr, ref context);
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is CastExpressionSyntax castExpr)
        {
            return castExpr.Expression;
        }

        var op = semanticModel.GetOperation(expression);
        if (op is IConversionOperation conv)
        {
            return conv.Operand.Syntax as ExpressionSyntax ?? expression;
        }

        return expression;
    }

    private static bool EmitMacroOperatorAssignment(
        AssignmentExpressionSyntax assignment,
        ExpressionSyntax rightNode,
        AttributeData macroAttr,
        ref TranspileContext context)
    {
        var targetMethod = ResolveTargetMethod(
            macroAttr,
            rightNode,
            out var targetType,
            out var targetMethodName,
            out var expectedMethodParamsMax);

        if (targetMethod == null)
        {
            if (targetType != null && targetMethodName != null)
            {
                context.Reporter.Report(
                    GSharpDiagnostics.UnsupportedSyntax,
                    assignment,
                    $"Macro target method '{targetType.Name}.{targetMethodName}' is missing or incompatible.");
            }

            return false;
        }

        var (leftOp, rightOp, extraArg) = ExtractOperands(rightNode);
        var resultOp = assignment.Left;
        var hasContext = targetMethod.Parameters.Length == expectedMethodParamsMax;

        context.Emitter.Writer.Append(targetMethodName);
        using (context.Emitter.Writer.Parentheses())
        {
            EmitMacroArguments(targetMethod, resultOp, leftOp, rightOp, extraArg, hasContext, ref context);
        }

        context.Emitter.Writer.AppendSemiColonLine();
        return true;
    }

    private static IMethodSymbol? ResolveTargetMethod(
        AttributeData macroAttr,
        ExpressionSyntax rightNode,
        out INamedTypeSymbol? targetType,
        out string? targetMethodName,
        out int expectedMethodParamsMax)
    {
        targetType = macroAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
        targetMethodName = macroAttr.ConstructorArguments[1].Value as string;

        if (targetType == null || targetMethodName == null)
        {
            expectedMethodParamsMax = 0;
            return null;
        }

        var expectedMethodParamsMin = rightNode is BinaryExpressionSyntax ? 3 : 2; // result + left + (right/extraArg)
        expectedMethodParamsMax = expectedMethodParamsMin + 1; // + context

        foreach (var member in targetType.GetMembers(targetMethodName))
        {
            if (member is IMethodSymbol m &&
                (m.Parameters.Length == expectedMethodParamsMin ||
                 (m.Parameters.Length == expectedMethodParamsMax && m.Parameters[0].Type.IsValueType)))
            {
                return m;
            }
        }

        return null;
    }

    private static (ExpressionSyntax? Left, ExpressionSyntax? Right, ExpressionSyntax? Extra) ExtractOperands(
        ExpressionSyntax rightNode)
    {
        if (rightNode is BinaryExpressionSyntax binaryExpr)
        {
            var left = binaryExpr.Left;
            if (!binaryExpr.IsKind(SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression))
            {
                return (left, binaryExpr.Right, null);
            }

            return (left, null, binaryExpr.Right);
        }

        if (rightNode is PrefixUnaryExpressionSyntax unaryExpr)
        {
            return (unaryExpr.Operand, null, null);
        }

        return (null, null, null);
    }

    private static void EmitMacroArguments(
        IMethodSymbol targetMethod,
        ExpressionSyntax resultOp,
        ExpressionSyntax? leftOp,
        ExpressionSyntax? rightOp,
        ExpressionSyntax? extraArg,
        bool hasContext,
        ref TranspileContext context)
    {
        var isFirst = true;
        var paramIndex = 0;

        if (hasContext)
        {
            appendArg(null, "ctx", ref context);
        }

        appendArg(resultOp, null, ref context);

        if (leftOp != null)
        {
            appendArg(leftOp, null, ref context);
        }

        if (rightOp != null)
        {
            appendArg(rightOp, null, ref context);
        }

        if (extraArg != null)
        {
            appendArg(extraArg, null, ref context);
        }

        void appendArg(ExpressionSyntax? expr, string? rawString, ref TranspileContext localContext)
        {
            if (!isFirst)
            {
                localContext.Emitter.Writer.AppendComma();
            }

            isFirst = false;

            var param = targetMethod.Parameters[paramIndex++];
            var isPointer = param.RefKind is RefKind.Ref or RefKind.Out or RefKind.In;

            if (isPointer && rawString != null)
            {
                localContext.Emitter.Writer.Append($"&{rawString}");
            }
            else if (rawString != null)
            {
                localContext.Emitter.Writer.Append(rawString);
            }
            else if (expr != null)
            {
                if (isPointer && RefSyntaxHelper.NeedsAddressOf(expr, localContext.Query.SemanticModel))
                {
                    localContext.Emitter.Writer.Append("&");
                }

                localContext.Visitor.Visit(expr);
            }
        }
    }
}
