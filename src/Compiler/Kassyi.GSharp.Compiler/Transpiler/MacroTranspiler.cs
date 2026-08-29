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
            symbol.ContainingType.Name != "Gpu" ||
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

        context.Writer.Append("for");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append($"int {paramName} = blockIdx.x * blockDim.x + threadIdx.x");
            context.Writer.AppendSemiColon();
            context.Writer.AppendSpace();
            context.Writer.Append(paramName);
            context.Writer.AppendOperator("<");
            context.Visitor.Visit(args[offset].Expression);
            context.Writer.AppendSemiColon();
            context.Writer.AppendSpace();
            context.Writer.Append(paramName);
            context.Writer.AppendOperator("+=");
            context.Writer.Append("gridDim.x * blockDim.x");
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

        context.Writer.Append("if");
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
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
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

        var isSupportedGpuBigIntOp = false;
        if (rightNode is BinaryExpressionSyntax binaryExpr)
        {
            var type = context.SemanticModel.GetTypeInfo(binaryExpr).Type;
            if (type is { Name: "GpuBigIntRef" })
            {
                isSupportedGpuBigIntOp = true;
            }
        }
        else if (rightNode is PrefixUnaryExpressionSyntax unaryExpr)
        {
            var type = context.SemanticModel.GetTypeInfo(unaryExpr).Type;
            if (type is { Name: "GpuBigIntRef" })
            {
                isSupportedGpuBigIntOp = true;
            }
        }

        if (!isSupportedGpuBigIntOp)
        {
            return false;
        }

        EmitGpuBigIntRefOperation(assignment, rightNode, ref context);
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

    private static void EmitGpuBigIntRefOperation(
        AssignmentExpressionSyntax assignment,
        ExpressionSyntax rightNode,
        ref TranspileContext context)
    {
        using (context.Writer.Scope())
        {
            context.Writer.Append("Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext _macroCtx");
            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();

            if (rightNode is BinaryExpressionSyntax binaryExpr)
            {
                EmitAssignment(ref context, "_macroCtx.Operands.LeftOperand", binaryExpr.Left);

                if (!binaryExpr.IsKind(SyntaxKind.LeftShiftExpression) &&
                    !binaryExpr.IsKind(SyntaxKind.RightShiftExpression))
                {
                    EmitAssignment(ref context, "_macroCtx.Operands.RightOperand", binaryExpr.Right);
                }
            }
            else if (rightNode is PrefixUnaryExpressionSyntax unaryExpr)
            {
                EmitAssignment(ref context, "_macroCtx.Operands.LeftOperand", unaryExpr.Operand);
            }

            EmitAssignment(ref context, "_macroCtx.Operands.ResultBuffer", assignment.Left);

            // Context buffers propagation (standard in Henicos BigInt kernels)
            context.Writer.Append("_macroCtx.Operands.CarryBuffer");
            context.Writer.AppendOperator("=");
            context.Writer.Append("ctx->Buffers.CarryBuffer");
            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();

            context.Writer.Append("_macroCtx.NumberOfWords");
            context.Writer.AppendOperator("=");
            context.Writer.Append("ctx->Dimensions.NumberOfWordsExtended");
            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();

            EmitDeviceFunctionCall(ref context, rightNode);
        }
    }

    private static void EmitAssignment(ref TranspileContext context, string target, ExpressionSyntax value)
    {
        context.Writer.Append(target);
        context.Writer.AppendOperator("=");
        context.Visitor.Visit(value);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    private static void EmitDeviceFunctionCall(ref TranspileContext context, ExpressionSyntax opNode)
    {
        ExpressionSyntax? extraArg = null;
        string? functionName = null;

        if (opNode is BinaryExpressionSyntax binaryExpr)
        {
            (functionName, extraArg) = binaryExpr.Kind() switch
            {
                SyntaxKind.AddExpression => ("Device_Add", (ExpressionSyntax?)null),
                SyntaxKind.SubtractExpression => ("Device_Sub", null),
                SyntaxKind.BitwiseAndExpression => ("Device_BitwiseAnd", null),
                SyntaxKind.BitwiseOrExpression => ("Device_BitwiseOr", null),
                SyntaxKind.ExclusiveOrExpression => ("Device_BitwiseXor", null),
                SyntaxKind.LeftShiftExpression => ("Device_ShiftLeft", binaryExpr.Right),
                SyntaxKind.RightShiftExpression => ("Device_ShiftRight", binaryExpr.Right),
                _ => (null, null)
            };
        }
        else if (opNode is PrefixUnaryExpressionSyntax unaryExpr)
        {
            functionName = unaryExpr.Kind() switch
            {
                SyntaxKind.BitwiseNotExpression => "Device_BitwiseNot",
                _ => null
            };
        }

        if (functionName == null)
        {
            return;
        }

        context.Writer.Append(functionName);
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("_macroCtx");
            if (extraArg != null)
            {
                context.Writer.AppendComma();
                context.Visitor.Visit(extraArg);
            }
        }

        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }
}




