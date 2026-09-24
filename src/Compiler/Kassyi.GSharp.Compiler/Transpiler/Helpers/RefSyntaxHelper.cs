using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Provides shared logic for handling 'ref', 'out', and pointer semantics in the AST.
/// </summary>
internal static class RefSyntaxHelper
{
    /// <summary>
    /// Determines whether the given expression requires an address-of operator (&amp;) when passed as a pointer or reference argument.
    /// Returns false if the expression is already a pointer or a ref-like local/parameter.
    /// </summary>
    public static bool NeedsAddressOf(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;

        if (symbol is IParameterSymbol { RefKind: not RefKind.None } ||
            symbol is ILocalSymbol { IsRef: true })
        {
            return false;
        }

        if (semanticModel.GetTypeInfo(expression).Type is IPointerTypeSymbol)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Transpiles the given expression as a reference/pointer argument, automatically appending the address-of operator if needed.
    /// </summary>
    public static void TranspileAsByRefArgument(ref TranspileContext context, ExpressionSyntax expression)
    {
        if (NeedsAddressOf(expression, context.SemanticModel))
        {
            context.Writer.Append("&");
        }

        context.Visitor.Visit(expression);
    }
}
