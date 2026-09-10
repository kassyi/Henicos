namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles method invocation argument syntax nodes into target dialect expressions,
/// appropriately emitting address-of operators (<c>&amp;</c>) for <see langword="ref"/> and <see langword="out"/> arguments.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ArgumentTranspilerRule : SyntaxNodeTranspilerRuleBase<ArgumentSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ArgumentTranspilerRule"/>.
    /// </summary>
    public static readonly ArgumentTranspilerRule Instance = new();

    private ArgumentTranspilerRule() { }

    /// <inheritdoc />
    public override void Transpile(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            context.Writer.Append("&");
        }

        context.Visitor.Visit(node.Expression);
    }
}
