using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Writes the <c>field</c> keyword in an accessor of a struct property as the backing field of the property
/// (<c>__gsharp_bf_&lt;property&gt;</c>, <see cref="CppNameResolver"/>), a member of the struct whose member function
/// the
/// accessor is.
/// </summary>
/// <remarks>
/// [WHY] The compiler builds against Roslyn 4.8, which has no <c>FieldExpressionSyntax</c> and no kind to register a
/// rule for. The syntax is checked first, without the semantic model: an expression whose only child is the token
/// <c>field</c> and that is not a name. The operation then confirms it: a reference to the implicitly declared backing
/// field of a property through the implicit <c>this</c>.
/// </remarks>
internal sealed class FieldKeywordInterceptor : IExpressionInterceptor
{
    private const string Keyword = "field";

    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (!IsKeywordOnly(expression) ||
            context.Query.SemanticModel.GetSafeOperation(expression) is not IFieldReferenceOperation
            {
                Field: { IsImplicitlyDeclared: true, AssociatedSymbol: IPropertySymbol } field,
                Instance.IsImplicit: true
            })
        {
            return false;
        }

        CppNameResolver.AppendSymbolName(field, ref context);
        return true;
    }

    // One token and no child node; a name spelled field (a local or a field of that name) is an IdentifierNameSyntax
    private static bool IsKeywordOnly(ExpressionSyntax expression) =>
        expression is not IdentifierNameSyntax &&
        expression.ChildNodesAndTokens() is [{ IsToken: true } token] &&
        token.AsToken().ValueText == Keyword;
}
