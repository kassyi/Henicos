
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;/// <summary>
/// Transpiles a C# argument into equivalent CUDA C++ representation.
/// Specifically handles reference arguments (<c>ref</c>, <c>out</c>) by prefixing them with the address-of operator <c>&amp;</c>.
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
    public ArgumentTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword))
        {
            RefSyntaxHelper.TranspileAsByRefArgument(ref context, node.Expression);
        }
        else
        {
            context.Visitor.Visit(node.Expression);
        }
    }
}
