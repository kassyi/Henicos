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
        if (!TryResolve(assignment, context.Query.SemanticModel, out var macro))
        {
            return false;
        }

        if (macro.TargetMethod is not { } targetMethod)
        {
            if (macro.IsTargetMissing)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, macro.FormatMissingTarget());
            }

            return false;
        }

        var (leftOp, rightOp, extraArg) = ExtractOperands(macro.OperatorCall);
        var resultOp = assignment.Left;
        var hasContext = targetMethod.Parameters.Length == macro.MaxParameterCount;

        context.Emitter.Writer.Append(macro.TargetMethodName);
        using (context.Emitter.Writer.Parentheses())
        {
            EmitMacroArguments(targetMethod, resultOp, leftOp, rightOp, extraArg, hasContext, ref context);
        }

        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    /// <summary>
    /// The <c>[GpuOperatorMacro]</c> operator that the right side of <paramref name="assignment"/> calls, and the method it
    /// maps to. The analyzer reports a missing or incompatible target (GSH0003) from the same answer.
    /// </summary>
    public static bool TryResolve(AssignmentExpressionSyntax assignment, SemanticModel semanticModel, out OperatorMacro macro)
    {
        macro = default;
        var rightNode = UnwrapExpression(assignment.Right, semanticModel);

        if (semanticModel.GetSymbol(rightNode) is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var macroAttr = methodSymbol.GetAttribute(KnownAttributes.GpuOperatorMacro);
        if (macroAttr?.AttributeClass?.ContainingNamespace.IsCoreNamespace() != true ||
            macroAttr.ConstructorArguments.Length < 2)
        {
            return false;
        }

        var targetMethod = ResolveTargetMethod(
            macroAttr,
            rightNode,
            out var targetType,
            out var targetMethodName,
            out var expectedMethodParamsMax);
        macro = new OperatorMacro(rightNode, targetType, targetMethodName, targetMethod, expectedMethodParamsMax);
        return true;
    }

    /// <summary>A call of a <c>[GpuOperatorMacro]</c> operator and the method it maps to.</summary>
    internal readonly struct OperatorMacro(
        ExpressionSyntax operatorCall,
        INamedTypeSymbol? targetType,
        string? targetMethodName,
        IMethodSymbol? targetMethod,
        int maxParameterCount)
    {
        /// <summary>The operator expression, without the conversion around it.</summary>
        public ExpressionSyntax OperatorCall { get; } = operatorCall;

        public INamedTypeSymbol? TargetType { get; } = targetType;

        public string? TargetMethodName { get; } = targetMethodName;

        /// <summary>The method the operator maps to, or <see langword="null"/> when it is missing or incompatible.</summary>
        public IMethodSymbol? TargetMethod { get; } = targetMethod;

        /// <summary>The parameter count of a target that takes a context first.</summary>
        public int MaxParameterCount { get; } = maxParameterCount;

        /// <summary>The attribute names a target, but no method of that type fits the operator.</summary>
        public bool IsTargetMissing => TargetMethod == null && TargetType != null && TargetMethodName != null;

        public string FormatMissingTarget() =>
            $"Macro target method '{TargetType?.Name}.{TargetMethodName}' is missing or incompatible.";
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
