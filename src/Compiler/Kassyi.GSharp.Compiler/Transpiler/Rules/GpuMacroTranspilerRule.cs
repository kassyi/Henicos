namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>Invocation transpiler rule that handles Gpu.InlinePtx and Gpu.JitConstant macro expansions.</summary>
internal sealed class GpuMacroTranspilerRule : IInvocationTranspilerRule
{
    public static readonly GpuMacroTranspilerRule Instance = new();

    private GpuMacroTranspilerRule()
    {
    }

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol.ContainingType.Name != "Gpu")
        {
            return false;
        }

        if (symbol.Name == "InlinePtx")
        {
            TranspileInlinePtx(node, ref context);
            return true;
        }

        if (symbol.Name == "JitConstant")
        {
            TranspileJitConstant(node, ref context);
            return true;
        }

        return false;
    }

    private static void TranspileInlinePtx(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        var argExpr = node.ArgumentList.Arguments[0].Expression;

        var constantValue = context.SemanticModel.GetConstantValue(argExpr);
        if (constantValue is { HasValue: true, Value: string ptxString })
        {
            context.Writer.Append(ptxString);
            context.Writer.AppendLine();
            return;
        }

        if (argExpr is InterpolatedStringExpressionSyntax interpolated)
        {
            TranspileInterpolatedInlinePtx(interpolated, ref context);
            return;
        }

        context.ReportDiagnostic?.Invoke(
            Diagnostic.Create(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, node.GetLocation()));
    }

    private static void TranspileInterpolatedInlinePtx(InterpolatedStringExpressionSyntax interpolated,
        ref TranspileContext context)
    {
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    context.Writer.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    context.Visitor.Visit(interpolation.Expression);
                    break;
            }
        }

        context.Writer.AppendLine();
    }

    private static void TranspileJitConstant(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments.Count == 1)
        {
            var expr = node.ArgumentList.Arguments[0].Expression;
            if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                context.Writer.Append(literal.Token.ValueText);
            }
            else if (expr is IdentifierNameSyntax identifier)
            {
                context.Writer.Append(identifier.Identifier.ValueText);
            }
        }
    }
}
