namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles method invocation syntax nodes into target dialect function calls,
/// coordinating with BCL intrinsic mappings and GPU macro expansions before defaulting to standard call emission.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class InvocationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<InvocationExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="InvocationExpressionTranspilerRule"/>.
    /// </summary>
    public static readonly InvocationExpressionTranspilerRule Instance = new();

    private static readonly ImmutableArray<IInvocationTranspilerRule> s_invocationRules =
        ImmutableArray.Create<IInvocationTranspilerRule>(
            BclIntrinsicTranspilerRule.Instance,
            GpuMacroTranspilerRule.Instance
        );

    private InvocationExpressionTranspilerRule() { }

    /// <inheritdoc />
    public override void Transpile(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        var handledExpression = false;
        if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
        {
            foreach (var rule in s_invocationRules)
            {
                if (rule.TryTranspile(node, ref context, symbol))
                {
                    return;
                }
            }

            if (MemberAccessExpressionTranspilerRule.TryEnsureNotProhibitedApi(symbol, node, ref context))
            {
                using (context.Writer.Parentheses())
                {
                    for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
                    {
                        context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                        if (i < node.ArgumentList.Arguments.Count - 1)
                        {
                            context.Writer.AppendComma();
                        }
                    }
                }
                return;
            }

            if (symbol.HasAttribute(KnownAttributes.GpuKernel) || symbol.HasAttribute(KnownAttributes.GpuDeviceFunction))
            {
                var mangledName = $"{symbol.ContainingType.Name}_{symbol.Name}";
                context.Writer.Append(mangledName);
                handledExpression = true;
            }
        }

        if (!handledExpression)
        {
            context.Visitor.Visit(node.Expression);
        }
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }
}
