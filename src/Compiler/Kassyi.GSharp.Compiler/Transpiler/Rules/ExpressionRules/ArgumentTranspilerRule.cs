namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles a C# argument into equivalent CUDA C++ representation.
/// Specifically handles reference arguments (`ref`, `out`) by prefixing them with the address-of operator `&`.
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

    private ArgumentTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword))
        {
            bool needsAddressOf = true;
            var symbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;

            if (symbol is IParameterSymbol { RefKind: not RefKind.None } ||
                symbol is ILocalSymbol { IsRef: true })
            {
                needsAddressOf = false;
            }
            else if (context.SemanticModel.GetTypeInfo(node.Expression).Type is IPointerTypeSymbol)
            {
                needsAddressOf = false;
            }

            if (needsAddressOf)
            {
                context.Writer.Append("&");
            }
        }

        context.Visitor.Visit(node.Expression);
    }
}
