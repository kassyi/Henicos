namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// A variable that can hold an array: a local or a parameter, or a chain of fields of one (<c>w.Inner.Data</c>, where
/// <c>w</c> is a struct local or parameter). Two keys are equal when they name the same symbols.
/// </summary>
internal readonly record struct AliasKey(ISymbol Variable, EquatableArray<IFieldSymbol> Path)
{
    /// <summary>The last field of the path, or <see cref="Variable"/> when the path is empty.</summary>
    public ISymbol Last => Path.IsDefaultOrEmpty ? Variable : Path[^1];

    /// <summary>A key for a variable without a field path.</summary>
    public static AliasKey ForVariable(ISymbol variable) =>
        new(variable, ImmutableArray<IFieldSymbol>.Empty.AsEquatableArray());

    /// <summary>
    /// The key of <paramref name="expression"/>: a local or a parameter, or a chain of non-static fields on one. A
    /// property, an element access or anything else has no key.
    /// </summary>
    public static AliasKey? Of(ExpressionSyntax expression, SemanticModel model)
    {
        expression = expression.UnwrapParentheses();
        if (expression is IdentifierNameSyntax)
        {
            return VariableOrNull(model.GetSymbol(expression)) is { } variable ? ForVariable(variable) : null;
        }

        if (expression is not MemberAccessExpressionSyntax)
        {
            return null;
        }

        var fields = ImmutableArray.CreateBuilder<IFieldSymbol>();
        var current = expression;
        while (current is MemberAccessExpressionSyntax access)
        {
            if (model.GetSymbol(access) is not IFieldSymbol { IsStatic: false } field)
            {
                return null;
            }

            fields.Add(field);
            current = access.Expression.UnwrapParentheses();
        }

        if (current is not IdentifierNameSyntax || VariableOrNull(model.GetSymbol(current)) is not { } holder)
        {
            return null;
        }

        fields.Reverse();
        return new AliasKey(holder, fields.ToImmutable().AsEquatableArray());
    }

    /// <summary>This key with <paramref name="field"/> appended to the path.</summary>
    public AliasKey Append(IFieldSymbol field) =>
        new(
            Variable,
            (Path.IsDefaultOrEmpty
                ? ImmutableArray.Create(field)
                : Path.AsImmutableArray().Add(field)).AsEquatableArray());

    /// <summary>
    /// Whether this key names a strict prefix of <paramref name="other"/>'s path on the same variable.
    /// </summary>
    public bool IsProperPrefixOf(AliasKey other)
    {
        if (!SymbolEqualityComparer.Default.Equals(Variable, other.Variable))
        {
            return false;
        }

        if (Path.Length >= other.Path.Length)
        {
            return false;
        }

        for (var i = 0; i < Path.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(Path[i], other.Path[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static ISymbol? VariableOrNull(ISymbol? symbol) =>
        symbol is ILocalSymbol or IParameterSymbol ? symbol : null;
}
