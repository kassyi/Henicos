using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Recognizes the <c>field</c> keyword in an accessor of a property: a read or write of the implicitly declared backing
/// field of the property through the implicit <c>this</c>.
/// </summary>
/// <remarks>
/// [WHY] The compiler builds against Roslyn 4.8, which has no <c>FieldExpressionSyntax</c> and no kind for it. The
/// syntax is checked first, without the semantic model: an expression whose only child is the token <c>field</c> and
/// that is not a name (a local or a field named <c>field</c> is an <see cref="IdentifierNameSyntax"/>). The operation
/// then confirms it. The transpiler (<c>FieldKeywordInterceptor</c>) and the effect analysis
/// (<see cref="ExpressionEffects"/>) share this one test.
/// </remarks>
internal static class FieldKeywordExpression
{
    private const string Keyword = "field";

    /// <summary>The backing field that <paramref name="expression"/> names, when it is the <c>field</c> keyword.</summary>
    public static bool TryGetBackingField(ExpressionSyntax expression, SemanticModel model,
        [NotNullWhen(true)] out IFieldSymbol? field)
    {
        field = IsKeywordOnly(expression) &&
                model.GetSafeOperation(expression) is IFieldReferenceOperation
                {
                    Field: { IsImplicitlyDeclared: true, AssociatedSymbol: IPropertySymbol } backingField,
                    Instance.IsImplicit: true
                }
            ? backingField
            : null;
        return field != null;
    }

    // One token and no child node
    private static bool IsKeywordOnly(ExpressionSyntax expression) =>
        expression is not IdentifierNameSyntax &&
        expression.ChildNodesAndTokens() is [{ IsToken: true } token] &&
        token.AsToken().ValueText == Keyword;
}
