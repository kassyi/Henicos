namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Identifies a declaration instance across AST lowering: its Roslyn symbol (or syntax name when no semantic model is
/// available) together with the 1-based expansion region ordinal (<c>0</c> outside expansions).
/// </summary>
public readonly record struct DeclarationKey(ISymbol? Symbol, string Name, int Region)
{
    public bool Equals(DeclarationKey other)
    {
        if (Region != other.Region)
        {
            return false;
        }

        if (Symbol is not null && other.Symbol is not null)
        {
            return Symbol.IsEqualTo(other.Symbol);
        }

        if (Symbol is null && other.Symbol is null)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        return false;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var symbolHash = Symbol is not null
                ? SymbolEqualityComparer.Default.GetHashCode(Symbol)
                : StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
            return (Region * 397) ^ symbolHash;
        }
    }
}
