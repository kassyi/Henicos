using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Writes the <c>field</c> keyword in an accessor of a struct property as the backing field of the property
/// (<c>__gsharp_bf_&lt;property&gt;</c>, <see cref="CppNameResolver"/>), a member of the struct whose member function the
/// accessor is.
/// </summary>
/// <remarks>
/// [WHY] The compiler builds against Roslyn 4.8, which has no <c>FieldExpressionSyntax</c> and no kind to register a
/// rule for, so the expression is recognized by what it binds to: a reference to the implicitly declared backing field
/// of a property through the implicit <c>this</c>. The keyword is the only expression of five characters that does.
/// </remarks>
internal sealed class FieldKeywordInterceptor : IExpressionInterceptor
{
    private const int KeywordLength = 5;

    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (expression.Span.Length != KeywordLength ||
            expression is IdentifierNameSyntax ||
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
}
