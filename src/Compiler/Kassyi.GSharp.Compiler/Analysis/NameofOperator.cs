namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The <c>nameof</c> operator, told apart from a call of a method that is named <c>nameof</c> (legal C#, which binds
/// to a method). A name inside the operator is read by nothing: its value is a constant string.
/// </summary>
internal static class NameofOperator
{
    private const string Keyword = "nameof";

    /// <summary>Whether <paramref name="node"/> is the <c>nameof</c> operator.</summary>
    public static bool Is(InvocationExpressionSyntax node, SemanticModel semanticModel) =>
        node.Expression is IdentifierNameSyntax { Identifier.ValueText: Keyword } &&
        semanticModel.GetSymbol(node) is not IMethodSymbol;

    /// <summary>Whether <paramref name="node"/> is inside the argument of a <c>nameof</c> operator.</summary>
    public static bool Encloses(SyntaxNode node, SemanticModel semanticModel) =>
        node.Ancestors()
            .AsStackEnumerable()
            .Any(semanticModel, static (ancestor, model) =>
                ancestor is InvocationExpressionSyntax invocation && Is(invocation, model));
}
