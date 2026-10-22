namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>Invocation transpiler rule that handles Gpu.InlinePtx and Gpu.Constant macro expansions.</summary>
internal sealed class GpuMacroTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!symbol.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        switch (GpuMacroClassifier.Classify(symbol).Kind)
        {
            case GpuMacroKind.InlinePtx:
                TranspileInlinePtx(node, ref context);
                return true;

            case GpuMacroKind.Constant:
                TranspileConstant(node, ref context);
                return true;

            default:
                return false;
        }
    }

    private static void TranspileInlinePtx(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        if (!context.Emitter.Dialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly))
        {
            var dialectName = context.Emitter.Dialect.GetType().Name;
            context.Reporter.Report(GSharpDiagnostics.InlinePtxNotSupportedByDialect, node, dialectName);
            context.Emitter.Writer.Append($"__gsharp_unsupported(\"Gpu.InlinePtx is not supported by the {dialectName} dialect\");");
            context.Emitter.Writer.AppendLine();
            if (context.Emitter.Dialect.IsCuda || !context.Emitter.IsGpuKernelEntry)
            {
                return;
            }

            context.Emitter.Writer.AppendLine(GSharpInternalNames.StateTermination);
            context.Emitter.Writer.AppendLine("return;");
            return;
        }

        var argExpr = node.ArgumentList.Arguments[0].Expression;

        var constantValue = context.Query.SemanticModel.GetSafeConstantValue(argExpr);
        if (constantValue is { HasValue: true, Value: string ptxString })
        {
            context.Emitter.Writer.Append(ptxString);
            context.Emitter.Writer.AppendLine();
            return;
        }

        if (argExpr is InterpolatedStringExpressionSyntax interpolated)
        {
            TranspileInterpolatedInlinePtx(interpolated, ref context);
            return;
        }

        context.Reporter.Report(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, node);
    }

    private static void TranspileInterpolatedInlinePtx(InterpolatedStringExpressionSyntax interpolated,
        ref TranspileContext context)
    {
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    context.Emitter.Writer.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    context.Visitor.Visit(interpolation.Expression);
                    break;
            }
        }

        context.Emitter.Writer.AppendLine();
    }

    private static void TranspileConstant(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments.Count == 1)
        {
            var expr = node.ArgumentList.Arguments[0].Expression;
            if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                context.Emitter.Writer.Append(literal.Token.ValueText);
            }
            else if (expr is IdentifierNameSyntax identifier)
            {
                context.Emitter.Writer.Append(identifier.Identifier.ValueText);
            }
        }
    }
}
