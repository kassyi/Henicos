namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles prefix unary expressions (<c>++x</c>, <c>--x</c>, <c>!x</c>, <c>~x</c>, <c>-x</c>, <c>+x</c>, and pointer indirection <c>*ptr</c>) into target dialect syntax.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class PrefixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PrefixUnaryExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="PrefixUnaryExpressionTranspilerRule"/>.
    /// </summary>
    public PrefixUnaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(PrefixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            context.Emitter.Writer.Append("*");
            context.Visitor.Visit(node.Operand);
            return;
        }

        if ((node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression)) &&
            ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Operand, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.IncrementOrDecrementOfComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        var op = node.OperatorToken.Text;
        context.Emitter.Writer.Append(op);
        context.Visitor.Visit(node.Operand);
    }
}
