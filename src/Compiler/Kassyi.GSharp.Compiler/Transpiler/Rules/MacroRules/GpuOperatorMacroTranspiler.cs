using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Intercepts and transpiles expressions decorated with <c>[GpuOperatorMacro]</c> into specialized device function calls.
/// </summary>
internal static class GpuOperatorMacroTranspiler
{
    /// <summary>
    /// Attempts to intercept and transpile an assignment expression decorated with a GPU operator macro.
    /// </summary>
    public static bool TryTranspile(AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        var rightNode = UnwrapExpression(assignment.Right, context.Query.SemanticModel);

        if (context.Query.SemanticModel.GetSymbol(rightNode) is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var macroAttr = methodSymbol.GetAttribute(KnownAttributes.GpuOperatorMacro);
        if (macroAttr?.AttributeClass?.ContainingNamespace.IsCoreNamespace() != true ||
            macroAttr.ConstructorArguments.Length < 2)
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

        var op = semanticModel.GetSafeOperation(expression);
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

        context.Emitter.Writer.AppendLine(";");
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

        return targetType.GetMembers(targetMethodName)
            .AsStackEnumerable()
            .Select(static member => member as IMethodSymbol)
            .FirstOrDefault(
                (Min: expectedMethodParamsMin, Max: expectedMethodParamsMax),
                static (m, expected) => m is not null &&
                    (m.Parameters.Length == expected.Min ||
                     (m.Parameters.Length == expected.Max && m.Parameters[0].Type.IsValueType))
            );
    }

    private static (ExpressionSyntax? Left, ExpressionSyntax? Right, ExpressionSyntax? Extra) ExtractOperands(
        ExpressionSyntax rightNode) => rightNode switch
        {
            BinaryExpressionSyntax { Left: var left, Right: var right } binary
                when binary.IsKind(SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression)
                => (left, null, right),
            BinaryExpressionSyntax { Left: var left, Right: var right }
                => (left, right, null),
            PrefixUnaryExpressionSyntax unary
                => (unary.Operand, null, null),
            _ => (null, null, null)
        };

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
                localContext.Emitter.Writer.Append(',');
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
