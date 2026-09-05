namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Expands high-level GSharp GPU macros (such as unrolled loops and grid-stride iterations) into CUDA C++ primitives.</summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Used implicitly by Roslyn compiler infra")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Used implicitly by Roslyn compiler infra")]
internal static class MacroTranspiler
{
    public static bool TryTranspileMacro(ExpressionStatementSyntax node, ref TranspileContext context)
    {
        return node.Expression switch
        {
            InvocationExpressionSyntax invocation => TryTranspileInvocation(invocation, ref context),
            AssignmentExpressionSyntax assignment => TryTranspileAssignment(assignment, ref context),
            _ => false
        };
    }

    private static bool TryTranspileInvocation(InvocationExpressionSyntax invocation, ref TranspileContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol ||
            symbol.ContainingType.Name != TypeConstants.GpuType ||
            !symbol.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        return symbol.Name switch
        {
            "ForUnroll" => TranspileForUnroll(invocation, ref context, symbol),
            "GridStrideLoop" => TranspileGridStrideLoop(invocation, ref context, symbol),
            "SingleThread" => TranspileSingleThread(invocation, ref context),
            _ => false
        };
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

        if (!TryGetConstantInt(args[offset].Expression, context.SemanticModel, out var start) ||
            !TryGetConstantInt(args[offset + 1].Expression, context.SemanticModel, out var count))
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnsupportedSyntax,
                invocation.GetLocation(),
                "ForUnroll arguments must be constants"));
            return false;
        }

        var paramName = ExtractUnrollLoopParameterName(lambdaExpr, hasContext);
        if (string.IsNullOrEmpty(paramName))
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.MacroLambdaRequiresParameter,
                invocation.GetLocation(),
                "ForUnroll"));
            return false;
        }

        string? contextParamName = null;
        if (hasContext && lambdaExpr is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            contextParamName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            var contextArgExpr = args[0].Expression;
            context.LocalVariableOverrides[contextParamName] = new OverrideValue(contextArgExpr);
        }

        context.Writer.AppendLine($"// [GpuUnroll] start={start}, count={count}");

        // [WHY] Unrolls the loop at C# source transpilation time by substituting the loop induction variable with literal constants, eliminating runtime branching in the GPU kernel.
        for (var i = start; i < start + count; i++)
        {
            context.LocalVariableOverrides[paramName] = new OverrideValue(i.ToString());
            EmitUnrolledBody(ref context, lambdaExpr.Body);
        }

        context.LocalVariableOverrides.Remove(paramName);
        if (hasContext && contextParamName != null)
        {
            context.LocalVariableOverrides.Remove(contextParamName);
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

        var paramName = ExtractUnrollLoopParameterName(lambdaExpr, hasContext);
        if (string.IsNullOrEmpty(paramName))
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.MacroLambdaRequiresParameter,
                invocation.GetLocation(),
                "GridStrideLoop"));
            return false;
        }

        string? contextParamName = null;
        if (hasContext && lambdaExpr is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            contextParamName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            var contextArgExpr = args[0].Expression;
            context.LocalVariableOverrides[contextParamName] = new OverrideValue(contextArgExpr);
        }

        // Emit: for (int {paramName} = blockIdx.x * blockDim.x + threadIdx.x; {paramName} < [EXPR]; {paramName} += gridDim.x * blockDim.x)
        context.Writer.Append($"for (int {paramName} = blockIdx.x * blockDim.x + threadIdx.x; {paramName} < ");
        context.Visitor.Visit(args[offset].Expression);
        context.Writer.Append($"; {paramName} += gridDim.x * blockDim.x)");

        context.Writer.AppendLine();
        EmitUnrolledBody(ref context, lambdaExpr.Body);

        if (hasContext && contextParamName != null)
        {
            context.LocalVariableOverrides.Remove(contextParamName);
        }

        return true;
    }

    private static bool TranspileSingleThread(InvocationExpressionSyntax invocation, ref TranspileContext context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        context.Writer.Append("if (threadIdx.x == 0 && blockIdx.x == 0)");

        context.Writer.AppendLine();
        EmitUnrolledBody(ref context, lambdaExpr.Body);

        return true;
    }

    private static void EmitUnrolledBody(ref TranspileContext context, SyntaxNode rewrittenBody)
    {
        using (context.Writer.Scope())
        {
            switch (rewrittenBody)
            {
                case BlockSyntax block:
                    foreach (var stmt in block.Statements)
                    {
                        context.Visitor.Visit(stmt);
                        context.Writer.AppendLine();
                    }

                    break;

                case ExpressionSyntax expr:
                    context.Visitor.Visit(expr);
                    context.Writer.AppendSemiColonLine();
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
        var rightNode = UnwrapExpression(assignment.Right, context.SemanticModel);

        var symbolInfo = context.SemanticModel.GetSymbolInfo(rightNode);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var macroAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "GpuOperatorMacroAttribute");

        if (macroAttr == null)
        {
            return false;
        }

        EmitMacroOperatorAssignment(assignment, rightNode, methodSymbol, macroAttr, ref context);
        return true;
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is CastExpressionSyntax castExpr)
        {
            return castExpr.Expression;
        }

        var op = semanticModel.GetOperation(expression);
        if (op is Microsoft.CodeAnalysis.Operations.IConversionOperation conv)
        {
            return conv.Operand.Syntax as ExpressionSyntax ?? expression;
        }

        return expression;
    }

    private static void EmitMacroOperatorAssignment(
        AssignmentExpressionSyntax assignment,
        ExpressionSyntax rightNode,
        IMethodSymbol methodSymbol,
        AttributeData macroAttr,
        ref TranspileContext context)
    {
        var targetType = (INamedTypeSymbol)macroAttr.ConstructorArguments[0].Value!;
        var targetMethodName = (string)macroAttr.ConstructorArguments[1].Value!;
        var targetMethod = targetType.GetMembers(targetMethodName).OfType<IMethodSymbol>().FirstOrDefault();

        if (targetMethod == null)
        {
            return;
        }

        ExpressionSyntax? leftOp = null;
        ExpressionSyntax? rightOp = null;
        ExpressionSyntax? extraArg = null;

        if (rightNode is BinaryExpressionSyntax binaryExpr)
        {
            leftOp = binaryExpr.Left;
            if (!binaryExpr.IsKind(SyntaxKind.LeftShiftExpression) &&
                !binaryExpr.IsKind(SyntaxKind.RightShiftExpression))
            {
                rightOp = binaryExpr.Right;
            }
            else
            {
                extraArg = binaryExpr.Right;
            }
        }
        else if (rightNode is PrefixUnaryExpressionSyntax unaryExpr)
        {
            leftOp = unaryExpr.Operand;
        }

        var resultOp = assignment.Left;

        context.Writer.AppendFullyQualifiedCppName(targetType).Append("::").Append(targetMethodName);
        using (context.Writer.Parentheses())
        {
            bool hasContext = targetMethod.Parameters.Length > 0 && targetMethod.Parameters[0].Type.IsValueType;
            bool isFirst = true;

            if (hasContext)
            {
                context.Writer.Append("ctx");
                isFirst = false;
            }

            if (!isFirst) context.Writer.AppendComma();
            context.Visitor.Visit(resultOp);
            isFirst = false;

            if (leftOp != null)
            {
                if (!isFirst) context.Writer.AppendComma();
                context.Visitor.Visit(leftOp);
                isFirst = false;
            }

            if (rightOp != null)
            {
                if (!isFirst) context.Writer.AppendComma();
                context.Visitor.Visit(rightOp);
                isFirst = false;
            }

            if (extraArg != null)
            {
                if (!isFirst) context.Writer.AppendComma();
                context.Visitor.Visit(extraArg);
            }
        }

        context.Writer.AppendSemiColonLine();
    }
}




