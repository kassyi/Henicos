namespace Kassyi.GSharp.Compiler.Transpiler;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Used implicitly by Roslyn compiler infra")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Used implicitly by Roslyn compiler infra")]
internal static class MacroTranspiler
{
    public static bool TryTranspileMacro(ExpressionStatementSyntax node, RoslynToCudaVisitor context)
    {
        return node.Expression switch
        {
            InvocationExpressionSyntax invocation => TryTranspileInvocation(invocation, context),
            AssignmentExpressionSyntax assignment => TryTranspileAssignment(assignment, context),
            _ => false
        };
    }

    private static bool TryTranspileInvocation(InvocationExpressionSyntax invocation, RoslynToCudaVisitor context)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol ||
            symbol.ContainingType.Name != "Gpu" ||
            symbol.ContainingNamespace.ToDisplayString() != "Kassyi.GSharp.Core")
        {
            return false;
        }

        return symbol.Name switch
        {
            "ForUnroll" => TranspileForUnroll(invocation, context, symbol),
            "GridStrideLoop" => TranspileGridStrideLoop(invocation, context),
            "SingleThread" => TranspileSingleThread(invocation, context),
            _ => false
        };
    }

    private static bool TranspileForUnroll(InvocationExpressionSyntax invocation, RoslynToCudaVisitor context,
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
        context.Writer.AppendLine($"// [GpuUnroll] start={start}, count={count}");

        for (var i = start; i < start + count; i++)
        {
            var rewriter = new UnrollRewriter(paramName, i);
            var rewrittenBody = rewriter.Visit(lambdaExpr.Body);
            EmitUnrolledBody(context, rewrittenBody);
        }

        return true;
    }

    private static bool TranspileGridStrideLoop(InvocationExpressionSyntax invocation, RoslynToCudaVisitor context)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 2 || args[1].Expression is not LambdaExpressionSyntax lambdaExpr)
        {
            return false;
        }

        var paramName = ExtractUnrollLoopParameterName(lambdaExpr, false);

        context.Writer.Append("for");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append($"int {paramName} = blockIdx.x * blockDim.x + threadIdx.x");
            context.Writer.AppendSemiColon();
            context.Writer.AppendSpace();
            context.Writer.Append(paramName);
            context.Writer.AppendOperator("<");
            context.Visit(args[0].Expression);
            context.Writer.AppendSemiColon();
            context.Writer.AppendSpace();
            context.Writer.Append(paramName);
            context.Writer.AppendOperator("+=");
            context.Writer.Append("gridDim.x * blockDim.x");
        }

        context.Writer.AppendLine();
        EmitUnrolledBody(context, lambdaExpr.Body);

        return true;
    }

    private static bool TranspileSingleThread(InvocationExpressionSyntax invocation, RoslynToCudaVisitor context)
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
        EmitUnrolledBody(context, lambdaExpr.Body);

        return true;
    }

    private static void EmitUnrolledBody(RoslynToCudaVisitor context, SyntaxNode rewrittenBody)
    {
        using (context.Writer.Scope())
        {
            switch (rewrittenBody)
            {
                case BlockSyntax block:
                    foreach (var stmt in block.Statements)
                    {
                        context.Visit(stmt);
                        context.Writer.AppendLine();
                    }

                    break;

                case ExpressionSyntax expr:
                    context.Visit(expr);
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

    private static bool TryTranspileAssignment(AssignmentExpressionSyntax assignment, RoslynToCudaVisitor context)
    {
        var rightNode = UnwrapExpression(assignment.Right, context.SemanticModel);
        if (rightNode is not BinaryExpressionSyntax binaryExpr)
        {
            return false;
        }

        var binaryType = context.SemanticModel.GetTypeInfo(binaryExpr).Type;
        if (binaryType is not { Name: "GpuBigIntRef" })
        {
            return false;
        }

        EmitGpuBigIntRefBinaryOperation(assignment, binaryExpr, context);
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

    private static void EmitGpuBigIntRefBinaryOperation(
        AssignmentExpressionSyntax assignment,
        BinaryExpressionSyntax binaryExpr,
        RoslynToCudaVisitor context)
    {
        using (context.Writer.Scope())
        {
            context.Writer.Append("Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext _macroCtx");
            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();

            EmitAssignment(context, "_macroCtx.Operands.LeftOperand", binaryExpr.Left);

            if (!binaryExpr.IsKind(SyntaxKind.LeftShiftExpression) &&
                !binaryExpr.IsKind(SyntaxKind.RightShiftExpression))
            {
                EmitAssignment(context, "_macroCtx.Operands.RightOperand", binaryExpr.Right);
            }

            EmitAssignment(context, "_macroCtx.Operands.ResultBuffer", assignment.Left);

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

            EmitDeviceFunctionCall(context, binaryExpr);
        }
    }

    private static void EmitAssignment(RoslynToCudaVisitor context, string target, ExpressionSyntax value)
    {
        context.Writer.Append(target);
        context.Writer.AppendOperator("=");
        context.Visit(value);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    private static void EmitDeviceFunctionCall(RoslynToCudaVisitor context, BinaryExpressionSyntax binaryExpr)
    {
        var (functionName, extraArg) = binaryExpr.Kind() switch
        {
            SyntaxKind.AddExpression => ("Device_Add", (ExpressionSyntax?)null),
            SyntaxKind.SubtractExpression => ("Device_Sub", null),
            SyntaxKind.BitwiseAndExpression => ("Device_BitwiseAnd", null),
            SyntaxKind.BitwiseOrExpression => ("Device_BitwiseOr", null),
            SyntaxKind.LeftShiftExpression => ("Device_ShiftLeft", binaryExpr.Right),
            SyntaxKind.RightShiftExpression => ("Device_ShiftRight", binaryExpr.Right),
            _ => (null, null)
        };

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
                context.Visit(extraArg);
            }
        }

        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    private class UnrollRewriter(string paramName, int iterationValue) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!string.IsNullOrEmpty(paramName) && node.Identifier.Text == paramName)
            {
                return SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(iterationValue))
                    .WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }
    }
}
