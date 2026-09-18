using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Expands high-level GSharp GPU macros (such as unrolled loops and grid-stride iterations) into CUDA C++ primitives.</summary>
// ReSharper disable once UnusedType.Global -- Used implicitly by Roslyn compiler infra
// ReSharper disable once UnusedMember.Global -- Used implicitly by Roslyn compiler infra
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
        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol ||
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
            _ => false
        };
    }

    private static bool TranspileHintUnroll(InvocationExpressionSyntax invocation, ExpressionStatementSyntax statement,
        ref TranspileContext context)
    {
        if (!GpuMacroArgumentRule.PrecedesSupportedLoop(statement))
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnrollHintMustPrecedeLoop,
                invocation.GetLocation()));
            return true;
        }

        var args = invocation.ArgumentList.Arguments;
        var factor = 0;
        if (args.Count > 0)
        {
            var factorExpr = args[0].Expression;
            var constantValue = context.SemanticModel.GetConstantValue(factorExpr);
            if (!constantValue.HasValue || constantValue.Value is not int intVal || intVal < 0)
            {
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                    GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant,
                    factorExpr.GetLocation()));
                return true;
            }

            factor = intVal;
        }

        if (factor > 0)
        {
            context.Writer.AppendLine($"#pragma unroll {factor}");
        }
        else
        {
            context.Writer.AppendLine("#pragma unroll");
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
        context.Writer.AppendKeyword("for");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append($"int {paramName} = blockIdx.x * blockDim.x + threadIdx.x; {paramName} < ");
            context.Visitor.Visit(args[offset].Expression);
            context.Writer.Append($"; {paramName} += gridDim.x * blockDim.x");
        }

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

        context.Writer.AppendKeyword("if");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("threadIdx.x == 0 && blockIdx.x == 0");
        }

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
            .FirstOrDefault(a =>
                a.AttributeClass?.Name == "GpuOperatorMacroAttribute" &&
                a.AttributeClass.ContainingNamespace.IsCoreNamespace());

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
        if (macroAttr.ConstructorArguments.Length < 2 ||
            macroAttr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType ||
            macroAttr.ConstructorArguments[1].Value is not string targetMethodName)
        {
            return false;
        }

        var expectedMethodParamsMin = rightNode is BinaryExpressionSyntax ? 3 : 2; // result + left + (right/extraArg)
        var expectedMethodParamsMax = expectedMethodParamsMin + 1; // + context

        IMethodSymbol? targetMethod = null;
        foreach (var member in targetType.GetMembers(targetMethodName))
        {
            if (member is not IMethodSymbol m ||
                (m.Parameters.Length != expectedMethodParamsMin &&
                 (m.Parameters.Length != expectedMethodParamsMax || !m.Parameters[0].Type.IsValueType)))
            {
                continue;
            }

            targetMethod = m;
            break;
        }

        if (targetMethod == null)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                GSharpDiagnostics.UnsupportedSyntax,
                assignment.GetLocation(),
                $"Macro target method '{targetType.Name}.{targetMethodName}' is missing or incompatible."));
            return false;
        }

        var (leftOp, rightOp, extraArg) = rightNode switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LeftShiftExpression,
                    SyntaxKind.RightShiftExpression) =>
                (binary.Left, null, (ExpressionSyntax?)binary.Right),
            BinaryExpressionSyntax binary =>
                (binary.Left, (ExpressionSyntax?)binary.Right, null),
            PrefixUnaryExpressionSyntax unary =>
                (unary.Operand, null, null),
            _ => ((ExpressionSyntax?)null, (ExpressionSyntax?)null, (ExpressionSyntax?)null)
        };

        var resultOp = assignment.Left;

        context.Writer.Append(targetMethodName);
        using (context.Writer.Parentheses())
        {
            var hasContext = targetMethod.Parameters.Length == expectedMethodParamsMax;
            var isFirst = true;
            var paramIndex = 0;

            void appendArg(ExpressionSyntax? expr, string? rawString, ref TranspileContext localContext)
            {
                if (!isFirst)
                {
                    localContext.Writer.AppendComma();
                }

                isFirst = false;

                var param = targetMethod.Parameters[paramIndex++];
                var isPointer = param.RefKind is RefKind.Ref or RefKind.Out or RefKind.In;

                if (rawString != null)
                {
                    if (isPointer)
                    {
                        localContext.Writer.Append('&');
                    }

                    localContext.Writer.Append(rawString);
                }
                else if (expr != null)
                {
                    var isExprPointer = localContext.SemanticModel.GetTypeInfo(expr).Type is IPointerTypeSymbol;
                    if (isPointer && !isExprPointer)
                    {
                        localContext.Writer.Append('&');
                    }

                    localContext.Visitor.Visit(expr);
                }
            }

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
        }

        context.Writer.AppendSemiColonLine();
        return true;
    }
}
