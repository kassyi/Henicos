namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// A variable that can hold an array: a local or a parameter, or a field of one (<c>w.Data</c>, where <c>w</c> is a
/// struct local or parameter). Two keys are equal when they name the same symbols.
/// </summary>
internal readonly record struct AliasKey(ISymbol Variable, ISymbol? Member)
{
    /// <summary>
    /// The key of <paramref name="expression"/>: a local or a parameter, or a field of one named on it. A property, an
    /// element access or anything else has no key.
    /// </summary>
    public static AliasKey? Of(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is IdentifierNameSyntax)
        {
            return IsVariable(model.GetSymbol(expression)) is { } variable ? new AliasKey(variable, null) : null;
        }

        if (expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax holder } access &&
            IsVariable(model.GetSymbol(holder)) is { } holderVariable &&
            model.GetSymbol(access) is IFieldSymbol { IsStatic: false } field)
        {
            return new AliasKey(holderVariable, field);
        }

        return null;
    }

    public bool Equals(AliasKey other) =>
        SymbolEqualityComparer.Default.Equals(Variable, other.Variable) &&
        SymbolEqualityComparer.Default.Equals(Member, other.Member);

    public override int GetHashCode() =>
        (SymbolEqualityComparer.Default.GetHashCode(Variable) * 397) ^
        (Member == null ? 0 : SymbolEqualityComparer.Default.GetHashCode(Member));

    private static ISymbol? IsVariable(ISymbol? symbol) => symbol is ILocalSymbol or IParameterSymbol ? symbol : null;
}
