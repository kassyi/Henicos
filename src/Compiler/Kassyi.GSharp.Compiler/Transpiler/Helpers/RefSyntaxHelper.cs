namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Provides shared logic for handling 'ref', 'out', and pointer semantics in the AST.
/// </summary>
internal static class RefSyntaxHelper
{
    /// <summary>
    /// Determines whether the given expression requires an address-of operator (&amp;) when passed as a pointer or reference argument.
    /// Returns false if the expression is already a pointer or an <c>in</c> parameter.
    /// </summary>
    /// <remarks>
    /// <c>ref</c>/<c>out</c> parameters and ref locals are emitted dereferenced as <c>(*x)</c> by
    /// <see cref="RoslynToCudaVisitor.VisitIdentifierName"/>, so they still require <c>&amp;</c> to recover the pointer (<c>&amp;(*x)</c>).
    /// </remarks>
    public static bool NeedsAddressOf(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbol(expression);

        if (symbol is IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter })
        {
            return false;
        }

        if (semanticModel.GetTypeSymbol(expression) is IPointerTypeSymbol)
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
        if (NeedsAddressOf(expression, context.Query.SemanticModel))
        {
            context.Emitter.Writer.Append("&");
        }

        context.Visitor.Visit(expression);
    }
}
