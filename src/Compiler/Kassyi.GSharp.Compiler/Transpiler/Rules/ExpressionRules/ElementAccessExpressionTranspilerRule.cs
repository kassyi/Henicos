namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles array or pointer indexing and element access expressions into target dialect bracket notation (<c>expr[index]</c>).
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ElementAccessExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ElementAccessExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ElementAccessExpressionTranspilerRule"/>.
    /// </summary>
    public ElementAccessExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ElementAccessExpressionSyntax node, ref TranspileContext context)
    {
        // An element of a null array throws NullReferenceException in C# (#706), after the index is evaluated
        if (ArrayNullCheck.TryAppendCheckedElement(node, ref context))
        {
            return;
        }

        context.Visitor.Visit(node.Expression);
        using (context.Emitter.Writer.Brackets())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i].Expression);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Emitter.Writer.Append(',');
                }
            }
        }
    }
}
