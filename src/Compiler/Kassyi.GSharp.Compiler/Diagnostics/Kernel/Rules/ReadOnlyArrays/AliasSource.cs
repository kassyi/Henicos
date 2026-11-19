namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// A source assigned to an <see cref="AliasKey"/>: either an explicit syntax expression or a field copied from
/// another struct <see cref="AliasKey"/>.
/// </summary>
internal readonly record struct AliasSource(ExpressionSyntax? Expression, AliasKey? Key)
{
    public static AliasSource FromExpression(ExpressionSyntax expression) => new(expression, null);

    public static AliasSource FromKey(AliasKey key) => new(null, key);
}
